// DRAFT !!! DRAFT !!! DRAFT !!!
// To be converted into an assistant

// Reference assemblies (commented out because they may not be needed in Tabular Editor)
//#r "System.Net.Http.dll"
//#r "Newtonsoft.Json.dll"

// Import namespaces
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
//using Microsoft.AnalysisServices.Tabular; // Uncomment if working with Analysis Services Tabular models

// Function to ensure DAX code can be safely added into JSON without causing formatting issues
public static string EscapeDaxCodeForJson(string daxCode)
{
    // Use JsonConvert.ToString to escape special characters and add quotes around the string
    string escapedDaxCode = JsonConvert.ToString(daxCode);
    
    // Remove the surrounding quotes if they exist
    if (escapedDaxCode.Length >= 2 && escapedDaxCode.StartsWith("\"") && escapedDaxCode.EndsWith("\""))
    {
        escapedDaxCode = escapedDaxCode.Substring(1, escapedDaxCode.Length - 2);
    }

    return escapedDaxCode;
}

// Build the data model metadata (including tables, columns, measures, and relationships)
var modelInfo = new StringBuilder();

modelInfo.AppendLine("Data Model Metadata:"); // Add a header for the metadata
modelInfo.AppendLine();

// Loop through all tables in the model
foreach(var table in Model.Tables)
{
    // Append table name
    modelInfo.AppendLine($"Table: {table.Name}");
    modelInfo.AppendLine("Columns:");

    // Loop through all columns in the table
    foreach(var column in table.Columns)
    {
        // Check if the column is a calculated column
        var calculatedColumn = column as CalculatedColumn;
        if (calculatedColumn != null)
        {
            // Append calculated column info (name, data type, and DAX expression)
            string columnName = calculatedColumn.Name;
            string dataType = column.DataType.ToString();
            string expression = EscapeDaxCodeForJson(calculatedColumn.Expression);
            modelInfo.AppendLine($"- Calculated Column: {columnName} ({dataType}) = {expression}");
        }
        else
        {
            // Append regular column info (name and data type only)
            string columnName = column.Name;
            string dataType = column.DataType.ToString();
            modelInfo.AppendLine($"- {columnName} ({dataType})");
        }
    }

    // If the table contains measures, append them as well
    if(table.Measures.Count > 0)
    {
        modelInfo.AppendLine("Measures:");

        foreach(var measure in table.Measures)
        {
            // Append measure name and DAX expression
            string measureName = measure.Name;
            string measureExpression = EscapeDaxCodeForJson(measure.Expression);
            modelInfo.AppendLine($"- {measureName} = {measureExpression}");
        }
    }

    modelInfo.AppendLine(); // Add a blank line between tables
}

// Append information about relationships between tables
modelInfo.AppendLine("Table Relationships:");
modelInfo.AppendLine();

// Loop through all relationships in the model
foreach(var relationship in Model.Relationships)
{
    // Append relationship details (from table/column, to table/column, and active/inactive status)
    string fromTable = relationship.FromTable.Name;
    string fromColumn = relationship.FromColumn.Name;
    string toTable = relationship.ToTable.Name;
    string toColumn = relationship.ToColumn.Name;
    string relationshipType = relationship.IsActive ? "Active" : "Inactive";

    modelInfo.AppendLine($"- {relationshipType} relationship from {fromTable}[{fromColumn}] to {toTable}[{toColumn}]");
}
modelInfo.AppendLine(); // Add a blank line after relationships

// Get the length of the model metadata string
int modelInfoLength = modelInfo.Length;

// Output the length of the metadata string in the Tabular Editor log
Info($"The length of modelInfo is {modelInfoLength} characters.");

// Get the first selected measure
var selectedMeasure = Selected.Measures.FirstOrDefault();

if (selectedMeasure == null)
{
    // If no measure is selected, show an error and stop the script
    Error("No measure is selected. Please select a measure and run the script again.");
    return;
}

// Get the DAX expression of the selected measure
string selectedMeasureExpression = selectedMeasure.Expression;

// Escape the selected measure expression for JSON formatting
string escapedMeasureExpression = EscapeDaxCodeForJson(selectedMeasureExpression);

// Set your OpenAI API key here. Keep it secure.
string apiKey = "OPEN_AI_API_KEY";

// Create an HTTP client to send requests to the OpenAI API
using (var client = new HttpClient())
{
    // Add the API key to the request header for authentication
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

    // Prepare the conversation messages for the OpenAI API
    var messages = new List<object>();

    // System message: Provide the data model metadata to OpenAI
    messages.Add(new
    {
        role = "system",
        content = $"You are an assistant that helps with DAX queries and is aware of the following data model:\n\n{modelInfo.ToString()}. Output format: valid DAX code, without measure name (only the expression), without any headers and footers around the measure. Use // for any comments. Avoid ```. Example of the output: SUM ( 'Table'[Column] )"
    });

    // User message: Request to comment on the selected DAX measure
    messages.Add(new
    {
        role = "user",
        content = $"Please add comments to this DAX measure: \n\nMeasure:\n```DAX\n{selectedMeasure.Name} = {escapedMeasureExpression}\n```"
    });

    // Prepare the body of the POST request
    var requestBody = new
    {
        model = "gpt-4o-mini", // Use GPT-4 or GPT-3.5 depending on access
        messages = messages
    };

    // Serialize the request body to JSON format
    var jsonRequestBody = JsonConvert.SerializeObject(requestBody);

    // Create HTTP content with JSON and proper encoding
    var content = new StringContent(jsonRequestBody, Encoding.UTF8, "application/json");

    // Send the POST request to OpenAI API asynchronously
    var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);

    if (response.IsSuccessStatusCode)
    {
        // If the response is successful, read and parse the response content
        var responseContent = await response.Content.ReadAsStringAsync();
        dynamic jsonResponse = JsonConvert.DeserializeObject(responseContent);

        // Extract the assistant's reply from the response
        string assistantReply = jsonResponse.choices[0].message.content.ToString();

        // Update the selected measure's expression with the assistant's reply
        selectedMeasure.Expression = assistantReply;

        // Format all DAX measures in the model for consistency
        Model.AllMeasures.FormatDax();

        // Output the assistant's reply in Tabular Editor
        Info("Assistant's Reply:\n\n" + assistantReply);
    }
    else
    {
        // If the request fails, log the status code and reason for failure
        Error($"Request failed with status code {response.StatusCode}: {response.ReasonPhrase}");
    }
}
