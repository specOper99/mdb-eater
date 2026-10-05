using System.Data.OleDb;
using MdbConverter.Core.Models;

namespace MdbConverter.Access;

internal static class AceConnectionFactory
{
    private static readonly string[] Providers =
    [
        "Microsoft.ACE.OLEDB.16.0",
        "Microsoft.ACE.OLEDB.15.0",
        "Microsoft.ACE.OLEDB.12.0"
    ];

    public static OleDbConnection Open(OpenOptions options)
    {
        Exception? last = null;
        foreach (var provider in Providers)
        {
            try
            {
                var connection = new OleDbConnection(Build(provider, options));
                connection.Open();
                return connection;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "Could not open the .mdb file. Install the Microsoft Access Database Engine 2016 x64 that matches this app.",
            last);
    }

    public static string Build(string provider, OpenOptions options)
    {
        var builder = new OleDbConnectionStringBuilder
        {
            Provider = provider,
            DataSource = options.MdbPath
        };
        builder["Persist Security Info"] = false;
        builder["Mode"] = "Share Deny None";

        if (!string.IsNullOrEmpty(options.DatabasePassword))
        {
            builder["Jet OLEDB:Database Password"] = options.DatabasePassword;
        }

        if (!string.IsNullOrWhiteSpace(options.WorkgroupPath))
        {
            builder["Jet OLEDB:System Database"] = options.WorkgroupPath;
        }

        if (!string.IsNullOrWhiteSpace(options.WorkgroupUser))
        {
            builder["User ID"] = options.WorkgroupUser;
        }

        if (!string.IsNullOrEmpty(options.WorkgroupPassword))
        {
            builder["Password"] = options.WorkgroupPassword;
        }

        return builder.ConnectionString;
    }
}

internal static class JetSql
{
    public static string Bracket(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
