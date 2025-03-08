using Newtonsoft.Json.Linq;
using Microsoft.Win32;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.IO;

// ================================================================================================
// This script interacts with OpenAI's Chat Completion API (https://platform.openai.com/docs/api-reference/chat) 
// and replaces the selected measure with an AI-generated version, including an AI-generated description and added comments.  
// A backup of the original measure is also created.
// ================================================================================================
// Author: Andrzej Leszkiewicz
// ================================================================================================

UseChatCompletionToUpdateTheSelectedMeasure();

//File path to save data model description in JSON format (optional, SaveDataModelJSON = true to enable)
bool SaveDataModelJSON = false;
const string DataModelFilePath = "D:\\DataModel.json";

void UseChatCompletionToUpdateTheSelectedMeasure()
{
    const string apiUrl = "https://api.openai.com/v1/chat/completions"; // API endpoint
    const string model = "gpt-4o-mini"; // Open AI model to use
    const double temperature = 0.4; // Higher values like 0.8 will make the output more random, while lower values like 0.2 will make it more focused and deterministic

    // Retrive API KEY from environment variable
    string apiKey = GetApiKey();
    if (string.IsNullOrEmpty(apiKey))
    {
        Error("OpenAI API key not found.");
        return;
    }

    // Generate JSON with data model metadata
    string dataModelContent = ExportModelToJsonString();
    if (string.IsNullOrEmpty(dataModelContent))
    {
        Error("Failed to generate DataModel.json.");
        return;
    }
    
    // Save the JSON as a file (optonal)
    if (SaveDataModelJSON) {
        //SaveDataModelToFile();
    }

    // Get selected measure
    var selectedMeasure = Selected.Measures.FirstOrDefault();
    if (selectedMeasure == null)
    {
        Error("No measure is selected. Please select a measure.");
        return;
    }

    string userQuery = GetUserQuery(selectedMeasure.Expression);
    
    var responseFormat = GetResponseFormat();

    var requestPayload = new
    {
        model,
        temperature,
        messages = new[]
        {
            new { role = "system", content = "You are an expert in DAX and Power BI modeling. Use the provided DataModel.json content for context." },
            new { role = "user", content = userQuery },
            new { role = "user", content = "Data Model JSON:\n" + dataModelContent }
        },
        response_format = responseFormat
    };

    // Get AI response
    string updatedDaxExpression = GetOpenAiResponse(apiUrl, apiKey, requestPayload);
    if (string.IsNullOrEmpty(updatedDaxExpression))
    {
        Error("Failed to retrieve a valid response from OpenAI.");
        return;
    }

    // Update the measure and create a backup copy
    BackupAndUpdateMeasure(selectedMeasure, updatedDaxExpression);
}

// ================================================================================================
// Get API KEY (from environment variable)
// ================================================================================================
string GetApiKey()
{
    using var userKey = Registry.CurrentUser.OpenSubKey(@"Environment");
    return userKey?.GetValue("OPENAI_TE_API_KEY") as string;
}

// ================================================================================================
// Chat prompt
// ================================================================================================
string GetUserQuery(string measureExpression) => 
        //$"list all tables form the DataModel.json file";
        $"Modify existing comments or add new comments to the given measure while strictly preserving the original DAX code." +
        $"Each comment should begin with '//' (for full-line comments) or '--' (for inline comments within a code line)." +
        $"Analyze the DataModel.json file to gain insights into the data model, its business context, tables, table relationships, measures." +
        $"Ensure the first few comment lines describe the measure's business purpose and context in simple terms." +
        $"Add a few lines with a technical explanation of the measure, detailing the purpose of referenced tables, columns, and measures." +
        $"Refer to the attached PDF files for a deeper understanding of the DAX language where necessary." +
        $"For every important DAX statement, insert a comment line **before** the code explaining its function." +
        $"Use inline comments ('--') only for additional clarification within a DAX line, avoiding excessive inline explanations." +
        $"If the measure contains any DAX syntax errors, correct them and add a comment explicitly stating what was changed and why." +
        $"Do not modify the core measure logic beyond necessary syntax corrections, and do not add extraneous text beyond required comments." +
        $"Do not use any special characters or tags to define the code block. Ensure the updated measure is a valid DAX expression." +
        $"Ensure the output strictly follows this JSON format: {{\"expression\":\"<put measure DAX expression here (and only here)>\",\"description\":\"<put measure description here>\",\"items\":\"<put a list of tables from DataModel>\",\"test\":\"<put a random joke here>\"}}." +
        $"The output must not contain any additional characters outside the JSON structure." +
        $"Add comments to short, single-line measures as well." +
        $"in the end of the measure add a comment with a list of tables in the data model." +
        $"Output a list of all tables in the datamodel (each as a comment line)." +
        $"Measure Expression: {measureExpression}";

// ================================================================================================
// Reponse format - JSON object
// ================================================================================================
object GetResponseFormat() => new
{
    type = "json_schema",
    json_schema = new
    {
        name = "response_json_schema",
        description = "JSON format of the response",
        strict = true,
        schema = new
        {
            type = "object",
            properties = new
            {
                expression = new { type = "string", description = "Measure DAX expression" },
                description = new { type = "string", description = "Measure description" }
            },
            required = new[] { "expression", "description" },
            additionalProperties = false
        }
    }
};

// ================================================================================================
// ================================================================================================
string GetOpenAiResponse(string apiUrl, string apiKey, object requestPayload)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    var requestBody = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(requestPayload), Encoding.UTF8, "application/json");
    var response = client.PostAsync(apiUrl, requestBody).Result;
    if (!response.IsSuccessStatusCode) return null;
    var responseContent = response.Content.ReadAsStringAsync().Result;
    return JObject.Parse(responseContent)?["choices"]?[0]?["message"]?["content"]?.ToString();
}

// ================================================================================================
// ================================================================================================
void BackupAndUpdateMeasure(dynamic selectedMeasure, string updatedDaxExpression)
{
    Guid newGuid = Guid.NewGuid();
    string backupMeasureName = $"{selectedMeasure.Name}_backup_{newGuid}";
    var newMeasure = selectedMeasure.Table.AddMeasure(backupMeasureName, selectedMeasure.Expression);
    newMeasure.DisplayFolder = selectedMeasure.DisplayFolder;
    newMeasure.Description = selectedMeasure.Description;
    newMeasure.FormatString = selectedMeasure.FormatString;

    try
    {
        JObject jsonOutput = JObject.Parse(updatedDaxExpression);
        selectedMeasure.Expression = jsonOutput["expression"].ToString();
        selectedMeasure.Description = jsonOutput["description"].ToString();
        selectedMeasure.FormatDax();
    }
    catch
    {
        Output(updatedDaxExpression);
    }
}

// ================================================================================================
// Function to export the current data model to a JSON string from memory
// ================================================================================================
string ExportModelToJsonString()
{
    var modelJson = new JObject();

    // Add tables and calculation groups to the JSON structure
    var tables = new JArray();
    foreach (var table in Model.Tables)
    {
        // Handle calculation groups differently from regular tables
        if (table is CalculationGroupTable calcGroupTable)
        {
            var calcGroupJson = GetCalculationGroupMetadata(calcGroupTable);
            tables.Add(calcGroupJson);
        }
        else
        {
            var tableJson = GetTableMetadata(table);
            tables.Add(tableJson);
        }
    }
    modelJson["tables"] = tables;

    // Add relationships to the JSON structure
    modelJson["relationships"] = GetRelationshipsMetadata();

    // Serialize the entire model to JSON with indentation for readability
    return Newtonsoft.Json.JsonConvert.SerializeObject(modelJson, Newtonsoft.Json.Formatting.Indented);
}

// ================================================================================================
// Helper function to get metadata for relationships
// ================================================================================================
JArray GetRelationshipsMetadata()
{
    var relationships = new JArray();
    foreach (var relationship in Model.Relationships)
    {
        var relJson = new JObject
        {
            { "name", relationship.Name },
            { "isActive", relationship.IsActive },
            { "crossFilteringBehavior", relationship.CrossFilteringBehavior.ToString() },
            { "securityFilteringBehavior", relationship.SecurityFilteringBehavior.ToString() },
            { "fromCardinality", relationship.FromCardinality.ToString() },
            { "toCardinality", relationship.ToCardinality.ToString() },
            { "fromColumn", relationship.FromColumn?.Name ?? "None" },
            { "fromTable", relationship.FromTable?.Name ?? "None" },
            { "toColumn", relationship.ToColumn?.Name ?? "None" },
            { "toTable", relationship.ToTable?.Name ?? "None" }
        };
        relationships.Add(relJson);
    }
    return relationships;
}

// Helper function to get metadata for regular tables
JObject GetTableMetadata(Table table)
{
    // Check if the table is a calculated table (DAX) by checking the partition's DAX expression
    bool isCalculatedTable = table.Partitions.Any(p => !string.IsNullOrEmpty(p.Expression) && p.SourceType == PartitionSourceType.Calculated);

    // Add basic table metadata
    var tableJson = new JObject
    {
        ["name"] = table.Name,
        ["description"] = table.Description ?? string.Empty,
        ["isCalculatedTable"] = isCalculatedTable
    };

    // Add the expression for both DAX-calculated tables and M query tables
    var partition = table.Partitions.FirstOrDefault();
    if (partition != null)
    {
        tableJson["expression"] = partition.Expression ?? string.Empty;
    }

    // Add columns to the table metadata
    var columns = new JArray();
    foreach (var column in table.Columns)
    {
        var columnJson = new JObject
        {
            ["name"] = column.Name,
            ["dataType"] = column.DataType.ToString(),
            ["dataCategory"] = column.DataCategory?.ToString() ?? "Uncategorized",
            ["description"] = column.Description ?? string.Empty,
            ["isHidden"] = column.IsHidden,
            ["formatString"] = column.FormatString ?? string.Empty,
            ["displayFolder"] = column.DisplayFolder ?? string.Empty,
            ["sortByColumn"] = column.SortByColumn != null ? column.SortByColumn.Name : "None"
        };

        // Add calculated column metadata
        if (column is CalculatedColumn calculatedColumn)
        {
            columnJson["isCalculatedColumn"] = true;
            columnJson["expression"] = calculatedColumn.Expression;
            columnJson["formatString"] = calculatedColumn.FormatString ?? string.Empty;
        }
        else
        {
            columnJson["isCalculatedColumn"] = false;
        }
        columns.Add(columnJson);
    }
    tableJson["columns"] = columns;

    // Add measures to the table metadata
    var measures = new JArray();
    foreach (var measure in table.Measures)
    {
        var measureJson = new JObject
        {
            ["name"] = measure.Name,
            ["description"] = measure.Description ?? string.Empty,
            ["expression"] = measure.Expression,
            ["formatString"] = measure.FormatString ?? string.Empty,
            ["isHidden"] = measure.IsHidden,
            ["displayFolder"] = measure.DisplayFolder ?? string.Empty
        };
        measures.Add(measureJson);
    }
    tableJson["measures"] = measures;

    return tableJson;
}

// Helper function to get metadata for calculation groups
JObject GetCalculationGroupMetadata(CalculationGroupTable calcGroupTable)
{
    var calcGroupJson = new JObject
    {
        ["name"] = calcGroupTable.Name,
        ["description"] = calcGroupTable.Description ?? string.Empty
    };

    // Add calculation items to the calculation group metadata
    var calcItems = new JArray();
    foreach (var item in calcGroupTable.CalculationItems)
    {
        var itemJson = new JObject
        {
            ["name"] = item.Name,
            ["expression"] = item.Expression
        };
        calcItems.Add(itemJson);
    }
    calcGroupJson["calculationItems"] = calcItems;

    // Add measures to the calculation group metadata
    var calcGroupMeasures = new JArray();
    foreach (var measure in calcGroupTable.Measures)
    {
        var measureJson = new JObject
        {
            ["name"] = measure.Name,
            ["description"] = measure.Description ?? string.Empty,
            ["expression"] = measure.Expression,
            ["formatString"] = measure.FormatString ?? string.Empty,
            ["isHidden"] = measure.IsHidden,
            ["displayFolder"] = measure.DisplayFolder ?? string.Empty
        };
        calcGroupMeasures.Add(measureJson);
    }
    calcGroupJson["measures"] = calcGroupMeasures;

    // Add calculated columns to the calculation group
    var calcColumns = new JArray();
    foreach (var column in calcGroupTable.Columns)
    {
        if (column is CalculatedColumn calculatedColumn)
        {
            var columnJson = new JObject
            {
                ["name"] = column.Name,
                ["dataType"] = column.DataType.ToString(),
                ["dataCategory"] = column.DataCategory?.ToString() ?? "Uncategorized",
                ["description"] = column.Description ?? string.Empty,
                ["isHidden"] = column.IsHidden,
                ["formatString"] = column.FormatString ?? string.Empty,
                ["displayFolder"] = column.DisplayFolder ?? string.Empty,
                ["sortByColumn"] = column.SortByColumn != null ? column.SortByColumn.Name : "None"
            };
            calcColumns.Add(columnJson);
        }
    }
    calcGroupJson["calculatedColumns"] = calcColumns;

    return calcGroupJson;
}

// Optional function to save the DataModel.json to disk
void SaveDataModelToFile(string jsonContent)
{
    try
    {
        // Write the JSON content to the file
        System.IO.File.WriteAllText(DataModelFilePath, jsonContent);
        Info($"DataModel.json has been saved to {DataModelFilePath}");
    }
    catch (System.Exception ex)
    {
        Error($"Failed to save DataModel.json: {ex.Message}");
    }
}

