// This section formats DAX code for all objects in the model:
// Measures, calculated tables, calculated columns, and calculated items

#r "System.Net.Http"
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;

// You need to signin to https://platform.openai.com/ and create an API key for your profile then paste that key into the apiKey constant below
var apiKey = "OPEN_AI_API_KEY";

const string uri = "https://api.openai.com/v1/chat/completions";

const string model = "gpt-4o";

var systemPrompt = "Please add comments to this DAX measure. Do not make any other changes than editing or adding comments. Return only valid DAX code, not comments from you about what you did, only comments regarding what code does.";

const string template = "{{\"model\": \"{0}\",\"messages\": [{{\"role\": \"system\",\"content\":[{{\"type\":\"text\",\"text\":\"{1}\"}}]}},{{\"role\": \"user\",\"content\":[{{\"type\":\"text\",\"text\":\"{2}\"}}]}}],\"temperature\": 1,\"max_tokens\": 4000}}";

// Format all measures in the model
Selected.Measures.FormatDax();  // Calls the FormatDax method on all measures to standardize their format

foreach (var m in Selected.Measures) {

    var expr = m.Expression; //measure expression
 
    using (var client = new HttpClient()) {

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);

        var userPrompt = expr;

System.Windows.Forms.MessageBox.Show  (m.Name);

System.Windows.Forms.MessageBox.Show (userPrompt);
    
        var body = string.Format(template, model, systemPrompt.Replace("\\", "\\\\").Replace("\"", "\\\""), userPrompt.Replace("\\", "\\\\").Replace("\"", "\\\""));

        var res = client.PostAsync(uri, new StringContent(body, Encoding.UTF8,"application/json"));

        res.Result.EnsureSuccessStatusCode();

        var result = res.Result.Content.ReadAsStringAsync().Result;

        var obj = JObject.Parse(result);
        var content = obj["choices"][0]["message"]["content"];
        var contentJson = content.ToString();

        var updatedExpr = contentJson;

System.Windows.Forms.MessageBox.Show (updatedExpr);

//alert expr;
//alert updatedExpr;

        m.Expression = updatedExpr;
        m.Description = updatedExpr;

System.Windows.Forms.MessageBox.Show (m.Expression);
    }
   
}

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
