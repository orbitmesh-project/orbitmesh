using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrbitMesh.Server.Configuration;
using OrbitMesh.Server.Services;

namespace OrbitMesh.Server.Tests;

/// <summary>The building blocks of the authentication, on their own.</summary>
public sealed class PasswordHasherTests
{
    [Fact]
    public void A_hash_carries_its_iteration_count_and_verifies_only_the_right_password()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");

        Assert.StartsWith("PWD$600000$", hash, StringComparison.Ordinal);
        Assert.True(PasswordHasher.IsHashed(hash));
        Assert.True(PasswordHasher.Verify(hash, "correct horse battery staple"));
        Assert.False(PasswordHasher.Verify(hash, "correct horse battery stapl"));
        Assert.False(PasswordHasher.Verify(hash, ""));
    }

    [Fact]
    public void Two_hashes_of_the_same_password_differ()
    {
        Assert.NotEqual(PasswordHasher.Hash("same password"), PasswordHasher.Hash("same password"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("PWD$")]
    [InlineData("PWD$notanumber$AAAA$AAAA")]
    [InlineData("PWD$600000$not base64$also not")]
    [InlineData("PWD$0$AAAA$AAAA")]
    [InlineData("PWD$-5$AAAA$AAAA")]
    public void Garbage_never_verifies_and_never_throws(string stored)
    {
        Assert.False(PasswordHasher.Verify(stored, "anything"));
    }

    [Fact]
    public void A_hash_made_with_fewer_iterations_is_marked_for_upgrade()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2("pw", salt, 100_000, HashAlgorithmName.SHA256, 32);
        var old = $"PWD$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";

        Assert.True(PasswordHasher.NeedsRehash(old));
        Assert.True(PasswordHasher.Verify(old, "pw"));
        Assert.False(PasswordHasher.NeedsRehash(PasswordHasher.Hash("pw")));
        Assert.False(PasswordHasher.NeedsRehash("not a hash"));
    }

    [Fact]
    public void The_legacy_key_is_the_lowercase_sha1_of_name_then_password()
    {
        // SHA-1("abc") - the form the previous Console computed and sent as its AccessKey.
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", PasswordHasher.LegacyAccessKey("a", "bc"));
    }

    [Fact]
    public void An_old_hash_still_verifies_the_old_derived_key_and_nothing_else()
    {
        var stored = AccessKeyHasher.Hash(PasswordHasher.LegacyAccessKey("boss", "pw"));

        Assert.True(AccessKeyHasher.IsHashed(stored));
        Assert.False(PasswordHasher.IsHashed(stored));
        Assert.True(AccessKeyHasher.Verify(stored, PasswordHasher.LegacyAccessKey("boss", "pw")));
        Assert.False(AccessKeyHasher.Verify(stored, "pw"));
    }
}

public sealed class OidcRoleMapperTests
{
    private static OidcOptions Provider(params (string Group, string[] Scopes)[] mappings) => new()
    {
        RoleMappings = mappings.Select(m => new OidcRoleMapping { Group = m.Group, Scopes = m.Scopes.ToList() }).ToList()
    };

    [Fact]
    public void A_star_means_every_scope_a_person_can_hold()
    {
        var scopes = OidcRoleMapper.Expand(["*"]);

        Assert.Equal(OrbitMeshScope.ForPeople.Order(), scopes.Order());
        Assert.DoesNotContain(OrbitMeshScope.EdgeConnect, scopes);
        Assert.DoesNotContain(OrbitMeshScope.PackageConnect, scopes);
        Assert.Contains(OrbitMeshScope.Developer, scopes);
    }

    [Fact]
    public void Unknown_scopes_and_machine_only_scopes_are_dropped()
    {
        var scopes = OidcRoleMapper.Expand(["edges:read", "made:up", OrbitMeshScope.EdgeConnect, OrbitMeshScope.PackageConnect, "EDGES:READ"]);

        Assert.Equal(["edges:read"], scopes);
    }

    [Fact]
    public void Several_matching_groups_give_the_union()
    {
        var provider = Provider(("a", ["edges:read"]), ("b", ["edges:read", "packages:read"]), ("c", ["credentials:manage"]));

        var scopes = OidcRoleMapper.ScopesFor(provider, ["a", "b"]);

        Assert.Equal(new[] { "edges:read", "packages:read" }.Order(), scopes.Order());
    }

    [Fact]
    public void Groups_match_regardless_of_case_and_unmapped_groups_give_nothing()
    {
        var provider = Provider(("Admins", ["*"]));

        Assert.NotEmpty(OidcRoleMapper.ScopesFor(provider, ["admins"]));
        Assert.Empty(OidcRoleMapper.ScopesFor(provider, ["users"]));
        Assert.Empty(OidcRoleMapper.ScopesFor(provider, []));
        Assert.Empty(OidcRoleMapper.ScopesFor(Provider(), ["admins"]));
    }

    [Theory]
    [InlineData(true, "https://idp", "orbitmesh", "secret", true)]
    [InlineData(false, "https://idp", "orbitmesh", "secret", false)]
    [InlineData(true, "", "orbitmesh", "secret", false)]
    [InlineData(true, "https://idp", "", "secret", false)]
    [InlineData(true, "https://idp", "orbitmesh", "", false)]
    public void A_provider_counts_as_usable_only_when_enabled_and_complete(bool enable, string authority, string clientId, string secret, bool usable)
    {
        var provider = new OidcOptions { Enable = enable, Authority = authority, ClientId = clientId, ClientSecret = secret };

        Assert.Equal(usable, provider.IsUsable);
    }

    [Fact]
    public void Machine_only_scopes_are_exactly_the_two_hub_connection_scopes()
    {
        Assert.Equal([OrbitMeshScope.EdgeConnect, OrbitMeshScope.PackageConnect], OrbitMeshScope.MachineOnly.Order());
        Assert.Equal(OrbitMeshScope.All.Count - 2, OrbitMeshScope.ForPeople.Count);
    }
}

public sealed class SessionStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "orbitmesh-tests", "unit-" + Guid.NewGuid().ToString("N"));

    public SessionStoreTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }

    private SessionStore NewStore() => new(new Env(root), new StaticOptions(new OrbitMeshOptions()), NullLogger<SessionStore>.Instance);

    [Fact]
    public void A_session_is_found_by_its_token_and_only_by_its_token()
    {
        var store = NewStore();
        var (token, session) = store.Create("alice", isFederated: false, groups: null, "10.0.0.1");

        Assert.Same(session, store.TryGetByToken(token));
        Assert.Same(session, store.TryGetById(session.Id));
        Assert.NotEqual(token, session.Id);
        Assert.Null(store.TryGetByToken(session.Id));
        Assert.Null(store.TryGetByToken("another-token"));
        Assert.Null(store.TryGetByToken(null));
        Assert.Null(store.TryGetByToken(""));
    }

    [Fact]
    public void Tokens_are_long_and_never_repeat()
    {
        var store = NewStore();

        var tokens = Enumerable.Range(0, 200).Select(_ => store.Create("alice", false, null, null).Token).ToList();

        Assert.Equal(200, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.True(t.Length >= 43, "32 random bytes encode to at least 43 characters"));
    }

    [Fact]
    public void Revoking_a_token_ends_that_session_only()
    {
        var store = NewStore();
        var (first, _) = store.Create("alice", false, null, null);
        var (second, _) = store.Create("alice", false, null, null);

        store.Revoke(first);

        Assert.Null(store.TryGetByToken(first));
        Assert.NotNull(store.TryGetByToken(second));
    }

    [Fact]
    public void Revoking_everything_for_a_name_spares_the_named_exception_and_other_people()
    {
        var store = NewStore();
        var (mine, mySession) = store.Create("alice", false, null, null);
        var (other, _) = store.Create("alice", false, null, null);
        var (bobs, _) = store.Create("bob", false, null, null);

        store.RevokeAllFor("alice", exceptId: mySession.Id);

        Assert.NotNull(store.TryGetByToken(mine));
        Assert.Null(store.TryGetByToken(other));
        Assert.NotNull(store.TryGetByToken(bobs));
    }

    [Fact]
    public void Maintenance_drops_sessions_that_idled_out()
    {
        var store = NewStore();
        var (token, session) = store.Create("alice", false, null, null);
        session.LastSeenUtc = DateTime.UtcNow.AddHours(-2);

        store.Maintain();

        Assert.Null(store.TryGetByToken(token));
    }

    [Fact]
    public void Sessions_survive_a_restart_without_the_token_ever_being_written()
    {
        var first = NewStore();
        var (token, session) = first.Create("alice", isFederated: true, groups: ["admins", "ops"], "10.0.0.1");

        var restarted = NewStore();

        var found = restarted.TryGetByToken(token);
        Assert.NotNull(found);
        Assert.Equal("alice", found.Name);
        Assert.True(found.IsFederated);
        Assert.Equal(["admins", "ops"], found.Groups);
        var file = File.ReadAllText(Path.Combine(root, "keys", "sessions.json"));
        Assert.DoesNotContain(token, file, StringComparison.Ordinal);
        Assert.Contains(session.Id, file, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_damaged_sessions_file_starts_the_store_empty_instead_of_failing()
    {
        Directory.CreateDirectory(Path.Combine(root, "keys"));
        File.WriteAllText(Path.Combine(root, "keys", "sessions.json"), "{ this is not json");

        var store = NewStore();

        Assert.Null(store.TryGetByToken("anything"));
        Assert.NotNull(store.Create("alice", false, null, null).Token);
    }

    private sealed class Env(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StaticOptions(OrbitMeshOptions value) : IOptionsMonitor<OrbitMeshOptions>
    {
        public OrbitMeshOptions CurrentValue => value;

        public OrbitMeshOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<OrbitMeshOptions, string?> listener) => null;
    }
}

public sealed class AccessKeyCipherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "orbitmesh-tests", "cipher-" + Guid.NewGuid().ToString("N"));

    public AccessKeyCipherTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }

    private AccessKeyCipher NewCipher() => new(new Env(root));

    [Fact]
    public void A_key_round_trips_and_is_never_stored_as_typed()
    {
        var cipher = NewCipher();

        var encrypted = cipher.Encrypt("my-machine-key");

        Assert.True(AccessKeyCipher.IsEncrypted(encrypted));
        Assert.DoesNotContain("my-machine-key", encrypted, StringComparison.Ordinal);
        Assert.Equal("my-machine-key", cipher.Decrypt(encrypted));
        Assert.NotEqual(encrypted, cipher.Encrypt("my-machine-key"));
    }

    [Fact]
    public void The_encryption_key_lives_in_its_own_file_and_is_reused()
    {
        var encrypted = NewCipher().Encrypt("key");

        Assert.True(File.Exists(Path.Combine(root, "keys", "credential-encryption.key")));
        Assert.Equal("key", NewCipher().Decrypt(encrypted));
    }

    [Fact]
    public void A_tampered_value_does_not_decrypt()
    {
        var cipher = NewCipher();
        var parts = cipher.Encrypt("my-machine-key").Split('$');
        var cipherBytes = Convert.FromBase64String(parts[2]);
        cipherBytes[0] ^= 0xFF;
        var tampered = $"ENC${parts[1]}${Convert.ToBase64String(cipherBytes)}${parts[3]}";

        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(tampered));
    }

    [Fact]
    public void Another_installations_key_cannot_read_the_value()
    {
        var encrypted = NewCipher().Encrypt("my-machine-key");
        var otherRoot = Path.Combine(root, "other");
        Directory.CreateDirectory(otherRoot);

        Assert.ThrowsAny<CryptographicException>(() => new AccessKeyCipher(new Env(otherRoot)).Decrypt(encrypted));
    }

    private sealed class Env(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
