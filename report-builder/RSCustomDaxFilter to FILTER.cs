// -----------------------------------------------------------------------------
// Replace RSCustomDaxFilter(...) with FILTER(...) placeholders
// Rewrite both table references and column references to use single-quoted table names.
// Keep original param name as a comment at the end of the FILTER expression.
// -----------------------------------------------------------------------------
//
// Usage: Select one or more objects in TE3, then run this script.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;

// Regex pattern to capture RSCustomDaxFilter(...)
string pattern = @"RSCustomDaxFilter\s*\(\s*@(?<param>[^,]+)\s*,\s*(?<cond>[^,]+)\s*,\s*(?<tablecol>\[[^\]]+\]\.\[[^\]]+\])\s*,\s*(?<dtype>[^)]+)\s*\)";

string ReplaceFunc(Match m)
{
    string param = m.Groups["param"].Value.Trim();
    string cond = m.Groups["cond"].Value.Trim();
    string tablecol = m.Groups["tablecol"].Value.Trim();

    // Split into table and column parts: [Table].[Column]
    string[] parts = tablecol.Split('.');
    string tablePart = parts[0].Trim('[', ']');   // remove [ ]
    string columnPart = parts[1].Trim('[', ']');  // remove [ ]

    // Build proper DAX references
    string tableName = $"'{tablePart}'";           // always quoted
    string columnRef = $"{tableName}[{columnPart}]";

    string daxOp = cond switch
    {
        "EqualToCondition" => "=",
        "NotEqualToCondition" => "<>",
        "GreaterThanCondition" => ">",
        "GreaterThanOrEqualToCondition" => ">=",
        "LessThanCondition" => "<",
        "LessThanOrEqualToCondition" => "<=",
        _ => "//UnknownCondition"
    };

    // Place comment at the end
    return $"FILTER( ALL({tableName}), {columnRef} {daxOp} \"<Placeholder>\") /* Param: {param} */";
}

// Loop through selected objects
foreach (var obj in Selected)
{
    // Only handle objects with an Expression property
    dynamic dynObj = obj;
    try
    {
        string dax = dynObj.Expression;
        if (string.IsNullOrWhiteSpace(dax)) continue;

        string newDax = Regex.Replace(dax, pattern, ReplaceFunc);

        if (newDax != dax)
        {
            dynObj.Expression = newDax;
            Output($"✅ Updated: {obj.Name}");
        }
    }
    catch
    {
        continue; // skip objects without Expression
    }
}
