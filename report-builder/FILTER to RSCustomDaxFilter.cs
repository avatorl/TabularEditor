// -----------------------------------------------------------------------------
// Replace FILTER(...) placeholders back into RSCustomDaxFilter(...)
// Assumes FILTER was generated in the style of the forward script:
//   FILTER( ALL('Table'), 'Table'[Column] OP "<Placeholder>") /* Param: ParamName */
// -----------------------------------------------------------------------------
//
// Usage: Select one or more objects in TE3, then run this script.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;

// Regex pattern to capture FILTER(...) with end-of-line param comment
string pattern = @"FILTER\s*\(\s*ALL\('(?<table>[^']+)'\)\s*,\s*'(?<table2>[^']+)'\[(?<column>[^\]]+)\]\s*(?<op>=|<>|>|>=|<|<=)\s*""<Placeholder>""\s*\)\s*/\*\s*Param:\s*(?<param>[^\*]+)\*/";

string ReplaceFunc(Match m)
{
    string table = m.Groups["table"].Value.Trim();
    string column = m.Groups["column"].Value.Trim();
    string op = m.Groups["op"].Value.Trim();
    string param = m.Groups["param"].Value.Trim();

    // Map operators back to RS conditions
    string cond = op switch
    {
        "="  => "EqualToCondition",
        "<>" => "NotEqualToCondition",
        ">"  => "GreaterThanCondition",
        ">=" => "GreaterThanOrEqualToCondition",
        "<"  => "LessThanCondition",
        "<=" => "LessThanOrEqualToCondition",
        _    => "UnknownCondition"
    };

    // Default DataType back to String (adjust if needed)
    string dtype = "String";

    // Build RSCustomDaxFilter call
    return $"RSCustomDaxFilter(@{param}, {cond}, [{table}].[{column}], {dtype})";
}

// Loop through selected objects
foreach (var obj in Selected)
{
    dynamic dynObj = obj;
    try
    {
        string dax = dynObj.Expression;
        if (string.IsNullOrWhiteSpace(dax)) continue;

        string newDax = Regex.Replace(dax, pattern, ReplaceFunc);

        if (newDax != dax)
        {
            dynObj.Expression = newDax;
            Output($"✅ Reverted: {obj.Name}");
        }
    }
    catch
    {
        continue; // skip objects without Expression
    }
}
