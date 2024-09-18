// This section formats DAX code for all objects in the model:
// Measures, calculated tables, calculated columns, and calculated items

// Format all measures in the model
Model.AllMeasures.FormatDax();

// Iterate through each table in the model to format DAX code
foreach (var table in Model.Tables) {
    // Format the DAX code for the table itself
    table.FormatDax();

    // Format the DAX code for each calculated column in the table
    foreach (var calculatedColumn in table.CalculatedColumns) {
        calculatedColumn.FormatDax();
    }
}

// Iterate through each calculation group table in the model
foreach (var calculationGroupTable in Model.Tables.OfType<CalculationGroupTable>()) {
    // Format the DAX code for each calculation item in the calculation group table
    foreach (var calculationItem in calculationGroupTable.CalculationItems) {
        calculationItem.FormatDax();
    }
}
