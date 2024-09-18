// Import necessary namespaces
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;


// Classes to represent the structure of the exported data model
public class ExportedModel
{
    public List<ExportedTable> Tables { get; set; }
    public List<ExportedRelationship> Relationships { get; set; }
}

public class ExportedTable
{
    public string TableName { get; set; }
    public List<ExportedColumn> Columns { get; set; }
    public List<ExportedMeasure> Measures { get; set; }
}

public class ExportedColumn
{
    public string ColumnName { get; set; }
    public string DataType { get; set; }
    public string Expression { get; set; }
}

public class ExportedMeasure
{
    public string MeasureName { get; set; }
    public string Expression { get; set; }
}

public class ExportedRelationship
{
    public string FromTable { get; set; }
    public string FromColumn { get; set; }
    public string ToTable { get; set; }
    public string ToColumn { get; set; }
    public bool IsActive { get; set; }
}

// Function to export the model to JSON as a string (instead of saving to a file)
public string ExportModelToJsonString()
{
    // Create the object that will hold the entire model structure
    var exportedModel = new ExportedModel
    {
        Tables = new List<ExportedTable>(),
        Relationships = new List<ExportedRelationship>()
    };

    // Loop through all tables in the model
    foreach (var table in Model.Tables)
    {
        // Create an ExportedTable object to hold table metadata
        var exportedTable = new ExportedTable
        {
            TableName = table.Name,
            Columns = new List<ExportedColumn>(),
            Measures = new List<ExportedMeasure>()
        };

        // Export columns
        foreach (var column in table.Columns)
        {
            var exportedColumn = new ExportedColumn
            {
                ColumnName = column.Name,
                DataType = column.DataType.ToString(),
                Expression = column is CalculatedColumn ? ((CalculatedColumn)column).Expression : null
            };
            exportedTable.Columns.Add(exportedColumn);
        }

        // Export measures
        foreach (var measure in table.Measures)
        {
            var exportedMeasure = new ExportedMeasure
            {
                MeasureName = measure.Name,
                Expression = measure.Expression
            };
            exportedTable.Measures.Add(exportedMeasure);
        }

        // Add the table to the exported model
        exportedModel.Tables.Add(exportedTable);
    }

    // Export relationships
    foreach (var relationship in Model.Relationships)
    {
        var exportedRelationship = new ExportedRelationship
        {
            FromTable = relationship.FromTable.Name,
            FromColumn = relationship.FromColumn.Name,
            ToTable = relationship.ToTable.Name,
            ToColumn = relationship.ToColumn.Name,
            IsActive = relationship.IsActive
        };

        // Add the relationship to the exported model
        exportedModel.Relationships.Add(exportedRelationship);
    }

    // Serialize the exported model to JSON
    return JsonConvert.SerializeObject(exportedModel, Formatting.Indented);
}

// Function to upload the model JSON content to OpenAI and create an assistant
public async Task CreateDaxAssistantAsync()
{
    // Replace with your OpenAI API key
    string apiKey = "OPEN_AI_API_KEY";
    string baseUrl = "https://api.openai.com/v1";
    
    // Create HttpClient instance
    using (var client = new HttpClient())
    {
        // Add the authorization header with the API key
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        // Add the required OpenAI-Beta header
        client.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");

        // Step 1: Export model data to JSON string
        string jsonContent = ExportModelToJsonString();

        // Convert the JSON string into a MemoryStream
        using (var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonContent)))
        {
            var formContent = new MultipartFormDataContent();
            var streamContent = new StreamContent(memoryStream);
            formContent.Add(streamContent, "file", "DataModel.json");
            formContent.Add(new StringContent("assistants"), "purpose");

            // Upload the file
            var uploadResponse = await client.PostAsync($"{baseUrl}/files", formContent);

            if (!uploadResponse.IsSuccessStatusCode)
            {
                var errorContent = await uploadResponse.Content.ReadAsStringAsync();
                Error($"File upload failed: {errorContent}");
                return;
            }

            var uploadResult = JsonConvert.DeserializeObject<dynamic>(await uploadResponse.Content.ReadAsStringAsync());
            string fileId = uploadResult.id.ToString();
            Info($"File uploaded successfully. File ID: {fileId}");

            // Step 2: Create and configure the assistant
            var assistantPayload = new
            {
                name = "Tabular Editor DAX Assistant",
                instructions = "You are a DAX assistant for Tabular Editor. When asked, you comment, optimize, or suggest DAX code. You're aware of the data model (the attached DataModel.json files provides data model description).",
                model = "gpt-4o",
                
                // Adding the code interpreter tool
                tools = new[] { new { type = "code_interpreter" } },
                
                // Adding the tool resources section with the file ID attached to the code_interpreter tool
                tool_resources = new
                {
                    code_interpreter = new
                    {
                        file_ids = new[] { fileId }
                    }
                }
            };

            var assistantRequestBody = new StringContent(JsonConvert.SerializeObject(assistantPayload), Encoding.UTF8, "application/json");
            var assistantResponse = await client.PostAsync($"{baseUrl}/assistants", assistantRequestBody);

            if (!assistantResponse.IsSuccessStatusCode)
            {
                var errorContent = await assistantResponse.Content.ReadAsStringAsync();
                Error($"Assistant creation failed: {errorContent}");
                return;
            }

            var assistantResult = JsonConvert.DeserializeObject<dynamic>(await assistantResponse.Content.ReadAsStringAsync());
            string assistantId = assistantResult.id.ToString();

            // Copy assistant ID to the clipboard
            Clipboard.SetText(assistantId);

            Info($"Assistant created successfully. Assistant ID: {assistantId} (copied to clipboard)");

        }
    }
}

// Execute the function to create the DAX assistant
await CreateDaxAssistantAsync();
