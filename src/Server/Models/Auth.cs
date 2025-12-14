namespace Server.Models;

public class SimpleAuthOptions
{
    public List<SimpleUser> Users { get; set; } = new();
}

public class SimpleUser
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string[] Roles { get; set; } = Array.Empty<string>();
}
