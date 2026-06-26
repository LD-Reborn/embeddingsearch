using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Novell.Directory.Ldap;
using Novell.Directory.Ldap.Sasl;
using Shared.Models;
using System.Security.Cryptography;
using System.Text;

namespace Shared.Services;

public class LdapAuthenticationService
{
    private readonly LdapOptions _options;
    private readonly ILogger<LdapAuthenticationService> _logger;

    public LdapAuthenticationService(IOptions<LdapOptions> options, ILogger<LdapAuthenticationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled =>
        !string.IsNullOrEmpty(_options.Host) &&
        _options.Port > 0 &&
        !string.IsNullOrEmpty(_options.BaseDn);

    public async Task<LdapAuthenticationResult?> AuthenticateAsync(string username, string password)
    {
        if (!IsEnabled)
            return null;

        try
        {
            using var connection = new LdapConnection();
            await connection.ConnectAsync(_options.Host, _options.Port, CancellationToken.None);

            if (_options.UseSsl)
                await connection.StartTlsAsync(CancellationToken.None);

            if (!string.IsNullOrEmpty(_options.BindDn))
                await connection.BindAsync(_options.BindDn, _options.BindPassword ?? "", CancellationToken.None);
            else
                await connection.BindAsync("", "", CancellationToken.None);

            var searchBase = string.IsNullOrEmpty(_options.UsersOu)
                ? _options.BaseDn
                : $"{_options.UsersOu},{_options.BaseDn}";

            var filter = $"({_options.UserSearchAttribute}={username})";

            var searchResults = await connection.SearchAsync(
                searchBase,
                LdapConnection.ScopeSub,
                filter,
                ["dn", "userPassword", "displayName"],
                false,
                CancellationToken.None
            );

            if (!await searchResults.HasMoreAsync(CancellationToken.None))
            {
                _logger.LogWarning("LDAP user not found: {Username}", username);
                return null;
            }

            var userEntry = await searchResults.NextAsync(CancellationToken.None);

            if (!await VerifyPasswordAsync(username, password, userEntry))
            {
                _logger.LogWarning("LDAP invalid password for user: {Username}", username);
                return null;
            }

            var displayName = userEntry.GetStringValueOrDefault("displayName", username);
            if (string.IsNullOrEmpty(displayName))
                displayName = username;

            return new LdapAuthenticationResult
            {
                Username = username,
                DisplayName = displayName,
                Roles = ["Admin"]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LDAP authentication error for user: {Username}", username);
            return null;
        }
    }

    private async Task<bool> VerifyPasswordAsync(string username, string password, LdapEntry userEntry)
    {
        var userPassword = userEntry.GetStringValueOrDefault("userPassword", null);
        if (userPassword is not null && userPassword.StartsWith('{'))
        {
            if (VerifyPasswordLocally(password, userPassword))
                return true;
        }

        try
        {
            using var userConnection = new LdapConnection();
            await userConnection.ConnectAsync(_options.Host, _options.Port, CancellationToken.None);

            if (_options.UseSsl)
                await userConnection.StartTlsAsync(CancellationToken.None);

            if (!string.IsNullOrEmpty(_options.SaslMechanism))
            {
                var saslRequest = CreateSaslRequest(username, password);
                await userConnection.BindAsync(saslRequest, CancellationToken.None);
            }
            else
            {
                await userConnection.BindAsync(userEntry.Dn, password, CancellationToken.None);
            }
            return true;
        }
        catch (LdapException)
        {
            return false;
        }
    }

    private static bool VerifyPasswordLocally(string password, string storedValue)
    {
        var closeBrace = storedValue.IndexOf('}');
        if (closeBrace < 1)
            return false;

        var scheme = storedValue[1..closeBrace].ToUpperInvariant();
        var encoded = storedValue[(closeBrace + 1)..];
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        try
        {
            var decodedHash = Convert.FromBase64String(encoded);

            return scheme switch
            {
                "SHA" or "SHA1" => VerifyHash(passwordBytes, decodedHash, SHA1.HashData, null),
                "SSHA" or "SSHA1" => VerifySaltedHash(passwordBytes, decodedHash, SHA1.HashData, 20),
                "SHA256" => VerifyHash(passwordBytes, decodedHash, SHA256.HashData, null),
                "SSHA256" => VerifySaltedHash(passwordBytes, decodedHash, SHA256.HashData, 32),
                "SHA512" => VerifyHash(passwordBytes, decodedHash, SHA512.HashData, null),
                "SSHA512" => VerifySaltedHash(passwordBytes, decodedHash, SHA512.HashData, 64),
                "MD5" => VerifyHash(passwordBytes, decodedHash, MD5.HashData, null),
                "SMD5" => VerifySaltedHash(passwordBytes, decodedHash, MD5.HashData, 16),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyHash(byte[] passwordBytes, byte[] expectedHash, Func<byte[], byte[]> hashFunc, int? _)
    {
        var computed = hashFunc(passwordBytes);
        return CryptographicOperations.FixedTimeEquals(computed, expectedHash);
    }

    private static bool VerifySaltedHash(byte[] passwordBytes, byte[] decoded, Func<byte[], byte[]> hashFunc, int hashLength)
    {
        if (decoded.Length <= hashLength)
            return false;

        var storedHash = decoded[..hashLength];
        var salt = decoded[hashLength..];

        var saltedInput = new byte[passwordBytes.Length + salt.Length];
        Buffer.BlockCopy(passwordBytes, 0, saltedInput, 0, passwordBytes.Length);
        Buffer.BlockCopy(salt, 0, saltedInput, passwordBytes.Length, salt.Length);

        var computed = hashFunc(saltedInput);
        return CryptographicOperations.FixedTimeEquals(computed, storedHash);
    }

    private SaslRequest CreateSaslRequest(string username, string password)
    {
        return (_options.SaslMechanism?.ToUpperInvariant()) switch
        {
            "DIGEST-MD5" => new SaslDigestMd5Request(username, password, _options.SaslRealm ?? "", _options.Host ?? ""),
            "CRAM-MD5" => new SaslCramMd5Request(username, password),
            "PLAIN" => new SaslPlainRequest(username, password),
            _ => throw new NotSupportedException($"SASL mechanism '{_options.SaslMechanism}' is not supported")
        };
    }
}

public class LdapAuthenticationResult
{
    public required string Username { get; set; }
    public string DisplayName { get; set; } = "";
    public List<string> Roles { get; set; } = [];
}
