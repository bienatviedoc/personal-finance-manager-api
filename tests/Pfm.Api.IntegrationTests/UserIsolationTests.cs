using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pfm.Application.Abstractions;
using Pfm.Domain;
using Pfm.Infrastructure.Persistence;

namespace Pfm.Api.IntegrationTests;

/// <summary>
/// Every user-owned row is visible and changeable only by its own user. No user-owned table exists
/// yet, so these tests use a test-only <see cref="Note"/> on a context derived from the real one: the
/// rule is the same convention every user-owned entity gets.
/// </summary>
[Collection(ApiCollection.Name)]
public class UserIsolationTests(PfmApi api) : IAsyncLifetime
{
    private readonly Guid _alice = Guid.CreateVersion7();
    private readonly Guid _bob = Guid.CreateVersion7();
    private DbContextOptions<PfmDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(api.ConnectionString) { Database = "isolation" }.ConnectionString;
        _options = new DbContextOptionsBuilder<PfmDbContext>().UseNpgsql(connection).Options;
        await using var db = As(null);
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_user_sees_only_their_own_rows()
    {
        var alicesNote = await AddNoteAsync(_alice, "Alice's");
        var bobsNote = await AddNoteAsync(_bob, "Bob's");

        await using var db = As(_alice);
        var visible = await db.Notes.Select(note => note.Id).ToListAsync();

        Assert.Contains(alicesNote, visible);
        Assert.DoesNotContain(bobsNote, visible);
    }

    [Fact]
    public async Task A_user_cannot_find_another_users_row_by_its_id()
    {
        var bobsNote = await AddNoteAsync(_bob, "Bob's");

        await using var db = As(_alice);

        Assert.Null(await db.Notes.FirstOrDefaultAsync(note => note.Id == bobsNote));
        Assert.Null(await db.Notes.FindAsync(bobsNote));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_user_cannot_change_another_users_row(bool claimingToOwnIt)
    {
        var bobsNote = await AddNoteAsync(_bob, "Bob's");

        await using (var db = As(_alice))
        {
            var forged = Forge(db, bobsNote, claimingToOwnIt ? _alice : _bob);
            forged.Text = "changed by Alice";
            await AssertRefusedAsync(db, claimingToOwnIt);
        }

        Assert.Equal("Bob's", await TextOfAsync(_bob, bobsNote));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_user_cannot_delete_another_users_row(bool claimingToOwnIt)
    {
        var bobsNote = await AddNoteAsync(_bob, "Bob's");

        await using (var db = As(_alice))
        {
            db.Notes.Remove(Forge(db, bobsNote, claimingToOwnIt ? _alice : _bob));
            await AssertRefusedAsync(db, claimingToOwnIt);
        }

        Assert.Equal("Bob's", await TextOfAsync(_bob, bobsNote));
    }

    [Fact]
    public async Task Bulk_changes_and_deletes_touch_only_the_users_own_rows()
    {
        var bobsNote = await AddNoteAsync(_bob, "Bob's");
        await AddNoteAsync(_alice, "Alice's");

        await using (var db = As(_alice))
        {
            await db.Notes.ExecuteUpdateAsync(setters => setters.SetProperty(note => note.Text, "bulk-changed"));
            await db.Notes.ExecuteDeleteAsync();
        }

        Assert.Equal("Bob's", await TextOfAsync(_bob, bobsNote));
    }

    [Fact]
    public async Task With_no_user_signed_in_there_is_nothing_to_see_and_nothing_can_be_added()
    {
        await AddNoteAsync(_alice, "Alice's");

        // What background jobs get: the host's default when nobody signs in.
        await using var db = new IsolationDbContext(_options, new NoCurrentUser());

        Assert.Empty(await db.Notes.ToListAsync());
        db.Notes.Add(new Note("nobody's"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task User_owned_data_is_filtered_by_the_user_the_session_cookie_belongs_to()
    {
        var (browser, _) = await api.SignedInAsync();
        var me = JsonSerializer.Deserialize<JsonElement>(await (await browser.GetAsync("/api/v1/me")).Content.ReadAsStringAsync());

        var filteredBy = await (await browser.GetAsync(PfmApi.CurrentUserPath)).Content.ReadAsStringAsync();

        Assert.Equal(me.GetProperty("id").GetString(), filteredBy);
    }

    [Fact]
    public async Task Every_user_owned_entity_is_isolated_by_the_signed_in_user()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var real = scope.ServiceProvider.GetRequiredService<PfmDbContext>();
        await using var withNotes = As(_alice);

        Assert.Empty(UnisolatedUserOwnedEntities(real));
        Assert.Empty(UnisolatedUserOwnedEntities(withNotes));
        Assert.Contains(withNotes.Model.GetEntityTypes(), type => type.ClrType == typeof(Note));
    }

    /// <summary>User-owned entity types missing the owner filter or the owner check on writes.</summary>
    private static string[] UnisolatedUserOwnedEntities(DbContext db) =>
        [.. db.Model.GetEntityTypes()
            .Where(type => typeof(IUserOwned).IsAssignableFrom(type.ClrType))
            .Where(type => type.GetDeclaredQueryFilters().Count == 0 || !type.FindProperty(nameof(IUserOwned.UserId))!.IsConcurrencyToken)
            .Select(type => type.ClrType.Name)];

    /// <summary>
    /// A forged row claiming Bob as owner is stopped by the owner check before anything is sent; one
    /// claiming Alice gets past it, but the owner is part of the UPDATE or DELETE condition, so the
    /// database changes nothing and EF reports it.
    /// </summary>
    private static async Task AssertRefusedAsync(IsolationDbContext db, bool claimingToOwnIt)
    {
        if (claimingToOwnIt)
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task A_row_added_for_someone_else_still_belongs_to_the_user_who_added_it()
    {
        Guid noteId;
        await using (var db = As(_alice))
        {
            var note = new Note("meant for Bob");
            typeof(Note).GetProperty(nameof(Note.UserId))!.SetValue(note, _bob);
            db.Notes.Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
        }

        Assert.Equal("meant for Bob", await TextOfAsync(_alice, noteId));
        Assert.Null(await TextOfAsync(_bob, noteId));
    }

    [Fact]
    public async Task A_user_cannot_give_their_row_to_another_user()
    {
        var alicesNote = await AddNoteAsync(_alice, "Alice's");

        await using (var db = As(_alice))
        {
            var note = await db.Notes.SingleAsync(row => row.Id == alicesNote);
            db.Entry(note).Property(row => row.UserId).CurrentValue = _bob;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        Assert.Equal("Alice's", await TextOfAsync(_alice, alicesNote));
        Assert.Null(await TextOfAsync(_bob, alicesNote));
    }

    [Fact]
    public async Task A_bulk_update_cannot_give_rows_to_another_user()
    {
        var alicesNote = await AddNoteAsync(_alice, "Alice's");

        await using (var db = As(_alice))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                db.Notes.Where(note => note.Id == alicesNote).ExecuteUpdateAsync(setters => setters.SetProperty(note => note.UserId, _bob)));
        }

        Assert.Equal("Alice's", await TextOfAsync(_alice, alicesNote));
        Assert.Null(await TextOfAsync(_bob, alicesNote));
    }

    /// <summary>
    /// A row attached without loading it, the way code can update or delete by id. It claims the
    /// given owner, so Alice can pretend Bob's row is hers.
    /// </summary>
    private static Note Forge(IsolationDbContext db, Guid id, Guid claimedOwner)
    {
        var note = new Note("forged");
        typeof(Note).GetProperty(nameof(Note.Id))!.SetValue(note, id);
        typeof(Note).GetProperty(nameof(Note.UserId))!.SetValue(note, claimedOwner);
        db.Notes.Attach(note);
        return note;
    }

    private async Task<string?> TextOfAsync(Guid user, Guid id)
    {
        await using var db = As(user);
        return await db.Notes.Where(note => note.Id == id).Select(note => note.Text).SingleOrDefaultAsync();
    }

    private async Task<Guid> AddNoteAsync(Guid user, string text)
    {
        await using var db = As(user);
        var note = new Note(text);
        db.Notes.Add(note);
        await db.SaveChangesAsync();
        return note.Id;
    }

    private IsolationDbContext As(Guid? user) => new(_options, new FixedCurrentUser(user));

    private sealed record FixedCurrentUser(Guid? UserId) : ICurrentUser;
}

public sealed class Note(string text) : IUserOwned
{
    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid UserId { get; private set; }

    public string Text { get; set; } = text;
}

public sealed class IsolationDbContext(DbContextOptions<PfmDbContext> options, ICurrentUser currentUser) : PfmDbContext(options, currentUser)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>();
        base.OnModelCreating(modelBuilder);
    }
}
