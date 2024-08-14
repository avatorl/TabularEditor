// This script exports all calculation groups (icluding calculation items and measures in the groups) into a C# script.
// The generated C# script can be used to restore these calculation groups in another model.

using System.IO;

// Define the output file path for the generated C# script
var file = @"d:\POWER BI - TOOLS & INFO\_TABULAR_SCRIPTS\Generated\Restore calculation groups (" + Model.Database.ID + ").csx";

var i = true; // Flag to determine when to use "var" for the first calculation group
var k = true; // Flag to determine when to use "var" for the first calculation item
var code = ""; // Variable to store the prefix ("var" or empty string) for declaring objects

// Create a StreamWriter to write the output C# script to the specified file
using (var fileWriter = new StreamWriter(file))
{
    // Loop through all CalculationGroupTables in the model
    foreach (var cg in Model.Tables.OfType<CalculationGroupTable>())
    {
        // Use "var" only for the first calculation group, omit it for subsequent ones
        if (i) { code = "var "; } else { code = ""; }

        // Generate the code to create the calculation group and set its properties
        var output = "\n\n// --- Begin creating calculation group: " + cg.Name + " ---";
        output = output + "\n" + code + "calculationGroupTable1 = Model.AddCalculationGroup();";
        output = output + "\n// Set the precedence level for the calculation group";
        output = output + "\n(Model.Tables[\"New Calculation Group\"] as CalculationGroupTable).CalculationGroup.Precedence = " + cg.CalculationGroup.Precedence + ";";
        output = output + "\n// Rename the calculation group to match the original name";
        output = output + "\ncalculationGroupTable1.Name = \"" + cg.Name + "\";";
        output = output + "\n// --- End creating calculation group: " + cg.Name + " ---";

        // Write the generated code for the calculation group to the file
        fileWriter.Write(output);

        i = false; // Reset the flag after the first calculation group

        // Loop through all CalculationItems in the current CalculationGroupTable
        foreach (var it in cg.CalculationItems)
        {
            // Use "var" only for the first calculation item, omit it for subsequent ones
            if (k) { code = "var "; } else { code = ""; }

            // Generate the code to create the calculation item and set its properties
            output = "\n\n// --- Begin creating calculation item: " + it.Name + " ---";
            output = output + "\n// Add a new calculation item to the calculation group";
            output = output + "\n" + code + "calculationItem1 = calculationGroupTable1.AddCalculationItem(\"" + it.Name + "\");";
            output = output + "\n// Set the DAX expression for the calculation item";
            output = output + "\ncalculationItem1.Expression = @\"" + it.Expression.Replace("\"", "\"\"") + "\";";

            // If a format string expression exists, add it to the generated code
            if (it.FormatStringExpression != null)
            {
                output = output + "\n// Set the format string expression if it exists";
                output = output + "\ncalculationItem1.FormatStringExpression = @\"" + it.FormatStringExpression.Replace("\"", "\"\"") + "\";";
            }

            output = output + "\n// Apply the default DAX formatting to the calculation item";
            output = output + "\ncalculationItem1.FormatDax();";
            output = output + "\n// --- End creating calculation item: " + it.Name + " ---";

            // Write the generated code for the calculation item to the file
            fileWriter.Write(output);

            k = false; // Reset the flag after the first calculation item
        }

        k = true; // Flag to determine when to use "var"

        // Loop through all Measures stored in the current CalculationGroupTable
        foreach (var measure in cg.Measures)
        {
            // Use "var" only for the first calculation item, omit it for subsequent ones
            if (k) { code = "var "; } else { code = ""; }

            // Generate the code to create the measure and set its properties
            output = "\n\n// --- Begin creating measure: " + measure.Name + " ---";
            output = output + "\n// Add a new measure to the calculation group";
            output = output + "\n" + code + "measure = calculationGroupTable1.AddMeasure(\"" + measure.Name + "\", \"" + measure.Expression.Replace("\"", "\"\"") + "\");";
            output = output + "\n// Set the display folder for the measure";
            output = output + "\nmeasure.DisplayFolder = \"" + measure.DisplayFolder + "\";";

            // If a format string exists, add it to the generated code
            if (!string.IsNullOrEmpty(measure.FormatString))
            {
                output = output + "\n// Set the format string for the measure";
                output = output + "\nmeasure1.FormatString = \"" + measure.FormatString + "\";";
            }

            output = output + "\n// --- End creating measure: " + measure.Name + " ---";

            // Write the generated code for the measure to the file
            fileWriter.Write(output);
        }
    }
}
