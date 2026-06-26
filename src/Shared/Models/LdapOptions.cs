namespace Shared.Models;

public class LdapOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 389;
    public bool UseSsl { get; set; }
    public string? BindDn { get; set; }
    public string? BindPassword { get; set; }
    public string? BaseDn { get; set; }
    public string? UsersOu { get; set; }
    public string UserSearchAttribute { get; set; } = "uid";
    public string? SaslMechanism { get; set; }
    public string? SaslRealm { get; set; }
}
