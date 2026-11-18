using System.Management;
using System.Runtime.Versioning;

namespace DriverCompass.Scanner.Windows;

/// <summary>Thin WMI abstraction so the mapping logic can be tested with fake rows.</summary>
public interface IWmiQuery
{
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wql);
}

/// <summary>Runs WQL queries against root\cimv2 (no administrator rights required).</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiQueryRunner : IWmiQuery
{
    private readonly ManagementScope _scope = new(@"\\.\root\cimv2");

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string wql)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(_scope, new ObjectQuery(wql), new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false });
        using var collection = searcher.Get();
        foreach (var item in collection)
        {
            using (item)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.Properties)
                {
                    row[property.Name] = property.Value;
                }

                rows.Add(row);
            }
        }

        return rows;
    }
}
