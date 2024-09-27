// ======================================================================================================================================================================
// This Tabular Editor C# script is designed to automate the setup of a dedicated measures table in a data model, specifically the _Measures table, 
//  and organize measures into predefined display folders (also known as display groups).
// © Andrzej Leszkiewicz https://www.linkedin.com/in/avatorl/
// ======================================================================================================================================================================

// List of display folders to create
var displayFolders = new List<string> {
    "00 Main Metrics",
    "10 Other Calculations",
    "70 Titles and Labels",
    "80 Filters",
    "90 Other Measures"
};

// Check if the _Measures table already exists
Table measuresTable = null;
foreach (var t in Model.Tables)
{
    if (t.Name == "_Measures")
    {
        measuresTable = t;
        break;
    }
}

if (measuresTable == null)
{
    // Create the calculated table using DATATABLE
    measuresTable = Model.AddCalculatedTable("_Measures", @"DATATABLE ( ""© Andrzej Leszkiewicz"", STRING, { { } } )");
}

// Get the column and hide it
var column = measuresTable.Columns["© Andrzej Leszkiewicz"];
if (column != null)
{
    column.IsHidden = true;
}

// Create a dummy measure in each display folder
foreach (var folder in displayFolders)
{
    // Define a unique measure name for the dummy measure
    var measureName = $"_{folder}";

    // Check if the measure already exists
    bool measureExists = false;
    foreach (var m in measuresTable.Measures)
    {
        if (m.Name == measureName)
        {
            measureExists = true;
            break;
        }
    }
    if (measureExists)
        continue;

    // Add a dummy measure
    var measure = measuresTable.AddMeasure(measureName, "0");
    measure.DisplayFolder = folder;
    measure.IsHidden = true; // Hide the dummy measure
}
