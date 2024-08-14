using System.IO;

// This script exports all calculation groups in the model into a C# script.
// The generated script can be used to restore these calculation groups and their items in another model.

var file = @"d:\POWER BI - TOOLS & INFO\_TABULAR_SCRIPTS\Generated\Restore calculation groups (" + Model.Database.ID + ").csx";

var i = true;  // Flag to control the 'var' keyword for the first calculation group
var k = true;  // Flag to control the 'var' keyword for the first calculation item
var code = ""; // Variable to store the code for 'var' keyword

// Create a StreamWriter to write the script to the specified file
using (var fileWriter = new StreamWriter(file))
{
    // Iterate through each Calculation Group in the model
    foreach (var cg in Model.Tables.OfType<CalculationGroupTable>())
    {
        // If it's the first calculation group, use 'var' for the variable declaration
        if (i) { code = "var "; } else { code = ""; }

        // Start writing the code to create the calculation group
        var output = "\n\n// Create calculation group " + cg.Name;
        output = output + "\n" + code + "calculationGroupTable1 = Model.AddCalculationGroup();";
        output = output + "\n(Model.Tables[\"New Calculation Group\"] as CalculationGroupTable).CalculationGroup.Precedence = " + cg.CalculationGroup.Precedence + ";";
        output = output + "\ncalculationGroupTable1.Name = \"" + cg.Name + "\";";

        // Write the output to the file
        fileWriter.Write(output);

        i = false; // Reset flag after the first iteration

        // Iterate through each Calculation Item within the current Calculation Group
        foreach (var it in cg.CalculationItems)
        {
            // If it's the first calculation item, use 'var' for the variable declaration
            if (k) { code = "var "; } else { code = ""; }

            // Start writing the code to create the calculation item
            output = "\n\n// Create calculation item " + it.Name;
            output = output + "\n" + code + "calculationItem1 = calculationGroupTable1.AddCalculationItem(\"" + it.Name + "\");";
            output = output + "\ncalculationItem1.Expression = @\"" + it.Expression.Replace("\"", "\"\"") + "\";";

            // If the calculation item has a format string expression, include it in the code
            if (it.FormatStringExpression != null)
            {
                output = output + "\ncalculationItem1.FormatStringExpression = @\"" + it.FormatStringExpression.Replace("\"", "\"\"") + "\";";
            }

            // Add a method call to format the DAX expression
            output = output + "\ncalculationItem1.FormatDax();";

            // Write the output to the file
            fileWriter.Write(output);

            k = false; // Reset flag after the first iteration
        }
    }
}
