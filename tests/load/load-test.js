// k6 load test: the API at full size (spec section 8). Lists must answer in under 300 ms and reports in under
// 1 s at the 95th percentile, for a user with 50,000 transactions. See "Load test" in the README for how to
// seed that user and run it.
//
//   k6 run -e PFM_URL=http://localhost:8080 tests/load/load-test.js
//
// Environment: PFM_URL (http://localhost:8080), PFM_EMAIL and PFM_PASSWORD (the user `seed` makes by default),
// VUS (10 users browsing at once) and DURATION (2m).
import http from 'k6/http';
import { check, fail } from 'k6';

const base = __ENV.PFM_URL || 'http://localhost:8080';
const api = `${base}/api/v1`;
const email = __ENV.PFM_EMAIL || 'sample@example.com';
const password = __ENV.PFM_PASSWORD || 'sample data password';

// Every list and report the app reads, each named so its own 95th percentile shows in the summary.
const lists = [
  'transactions',
  'transactions next page',
  'transactions this month',
  'transactions in a category',
  'transactions searched',
  'transactions by amount',
  'accounts',
  'institutions',
  'categories',
  'budgets',
  'recurring rules',
];
const reports = [
  'spending this month',
  'spending this year',
  'spending last 12 months compared',
  'spending in a category',
  'spending transactions',
  'trend of a category compared',
  'trend of a payee',
  'income vs expenses this year',
  'income vs expenses last 12 months compared',
  'net worth last 12 months',
  'budget vs actual this month',
  'budget vs actual this year',
];

export const options = {
  scenarios: {
    browsing: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 10),
      duration: __ENV.DURATION || '2m',
    },
  },
  thresholds: {
    'http_req_duration{kind:list}': ['p(95)<300'],
    'http_req_duration{kind:report}': ['p(95)<1000'],
    ...Object.fromEntries(lists.map((name) => [`http_req_duration{name:${name}}`, ['p(95)<300']])),
    ...Object.fromEntries(reports.map((name) => [`http_req_duration{name:${name}}`, ['p(95)<1000']])),
    checks: ['rate>0.99'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'max'],
};

const iso = (date) => date.toISOString().slice(0, 10);

/** Signs in once, and hands every virtual user the session and what the requests need. */
export function setup() {
  const antiforgery = http.get(`${api}/auth/antiforgery`);
  const signedIn = http.post(`${api}/auth/login`, JSON.stringify({ email, password }), {
    headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': antiforgery.json('token') },
  });
  if (signedIn.status !== 204) {
    fail(`Signing in as ${email} answered ${signedIn.status}: seed the user first.`);
  }

  const read = (path) => http.get(`${api}${path}`).json();
  const total = read('/transactions?limit=1').total.count;
  console.log(`Signed in as ${email}, who has ${total} transactions.`);
  const categories = read('/categories').items.flatMap((category) => [category, ...category.subcategories]);
  const food = categories.find((category) => category.name === 'Food & drink');
  const groceries = categories.find((category) => category.name === 'Groceries');
  if (!food || !groceries) {
    fail('The user has no Food & drink or Groceries category: seed them with Pfm.Api seed.');
  }

  return {
    cookies: http.cookieJar().cookiesForURL(base),
    foodId: food.id,
    groceriesId: groceries.id,
  };
}

function get(path, kind, name) {
  const response = http.get(`${api}${path}`, { tags: { kind, name } });
  check(response, { [`${name} answers 200`]: (r) => r.status === 200 });
  return response;
}

export default function (data) {
  // k6 empties each virtual user's cookie jar before every iteration, so the session goes back in each time.
  const jar = http.cookieJar();
  for (const [name, values] of Object.entries(data.cookies)) {
    jar.set(base, name, values[0]);
  }

  const today = new Date();
  const monthStart = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 1));
  const yearStart = new Date(Date.UTC(today.getUTCFullYear(), 0, 1));
  const yearAgo = new Date(Date.UTC(today.getUTCFullYear() - 1, today.getUTCMonth(), 1));
  const lastMonthEnd = new Date(monthStart.getTime() - 86400000);
  const thisMonth = `from=${iso(monthStart)}&to=${iso(today)}`;
  const thisYear = `from=${iso(yearStart)}&to=${iso(today)}`;
  const last12Months = `from=${iso(yearAgo)}&to=${iso(lastMonthEnd)}`;

  const first = get('/transactions', 'list', 'transactions');
  const cursor = first.status === 200 ? first.json('nextCursor') : null;
  if (cursor) {
    get(`/transactions?cursor=${encodeURIComponent(cursor)}`, 'list', 'transactions next page');
  }
  get(`/transactions?${thisMonth}`, 'list', 'transactions this month');
  get(`/transactions?categoryId=${data.foodId}`, 'list', 'transactions in a category');
  get('/transactions?q=coffee', 'list', 'transactions searched');
  get('/transactions?sort=amount&order=desc', 'list', 'transactions by amount');
  get('/accounts', 'list', 'accounts');
  get('/institutions', 'list', 'institutions');
  get('/categories', 'list', 'categories');
  get('/budgets', 'list', 'budgets');
  get('/recurring-rules', 'list', 'recurring rules');

  get(`/reports/spending-by-category?${thisMonth}`, 'report', 'spending this month');
  get(`/reports/spending-by-category?${thisYear}`, 'report', 'spending this year');
  get(`/reports/spending-by-category?${last12Months}&compare=true`, 'report', 'spending last 12 months compared');
  get(`/reports/spending-by-category?${thisYear}&parentId=${data.foodId}`, 'report', 'spending in a category');
  get(`/reports/spending-by-category/transactions?${thisYear}&categoryId=${data.groceriesId}`, 'report', 'spending transactions');
  get(`/reports/trends?${last12Months}&categoryId=${data.foodId}&compare=true`, 'report', 'trend of a category compared');
  get(`/reports/trends?${thisYear}&payee=WinMart`, 'report', 'trend of a payee');
  get(`/reports/income-vs-expenses?${thisYear}`, 'report', 'income vs expenses this year');
  get(`/reports/income-vs-expenses?${last12Months}&compare=true`, 'report', 'income vs expenses last 12 months compared');
  get(`/reports/net-worth?${last12Months}`, 'report', 'net worth last 12 months');
  get(`/reports/budget-vs-actual?${thisMonth}`, 'report', 'budget vs actual this month');
  get(`/reports/budget-vs-actual?${thisYear}`, 'report', 'budget vs actual this year');
}
