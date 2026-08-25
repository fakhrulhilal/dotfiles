namespace Dotfiles.Models;

public sealed class DbConfig {
    public required DbType Type { get; set; }
    public required string ConnectionString { get; set; }
}

public enum DbType {
    Sqlite = 1,
    Postgre = 2
}
