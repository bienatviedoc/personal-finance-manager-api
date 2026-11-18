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
            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
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
            await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
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

        await using var db = As(null);

        Assert.Empty(await db.Notes.ToListAsync());
        db.Notes.Add(new Note("nobody's"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Every_user_owned_entity_is_isolated_by_the_signed_in_user()
    {
        await using var real = api.Services.CreateAsyncScope().ServiceProvider.GetRequiredService<PfmDbContext>();
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
