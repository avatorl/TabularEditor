// This section formats DAX code for all objects in the model:
// Measures, calculated tables, calculated columns, and calculated items (including format string expressions).

// Format all measures in the model
Model.AllMeasures.FormatDax();  // Calls the FormatDax method on all measures to standardize their format

// Iterate through each table in the model to format its DAX code
foreach (var t in Model.Tables) {
    // Format the DAX code for the current table
    t.FormatDax();
    
    // Iterate through each calculated column in the current table
    foreach (var cc in t.CalculatedColumns) {
        // Format the DAX code for the calculated column
        cc.FormatDax();    
    }
}

// Iterate through each calculation group table in the model
foreach (var cg in Model.Tables.OfType<CalculationGroupTable>()) {
    // Iterate through each calculation item in the current calculation group
    foreach (var ci in cg.CalculationItems) {
        // Format the DAX code for the calculation item
        ci.FormatDax();
    }
}
