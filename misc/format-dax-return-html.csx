// Required namespaces
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;

// ----------------------------
// Helper Functions
// ----------------------------

/// Displays an error message to the user.
/// <param name="message">The error message to display.</param>
void ShowError(string message)
{
    MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
}


/// Extracts formatted HTML segments from the formatter service response using regex.
/// <param name="response">The HTML response string from the formatter service.</param>
/// <returns>A list of formatted HTML segments.</returns>
List<string> ExtractFormattedHtml(string response)
{
    var formattedHtmlList = new List<string>();
    // Regex pattern to capture the desired HTML content
    var regexPattern = @"<div.*?><div.*?>(.*?)<\/div><\/div>";

    foreach (Match match in Regex.Matches(response, regexPattern))
    {
        if (match.Groups.Count > 1)
        {
            formattedHtmlList.Add(match.Groups[1].Value);
        }
    }

    return formattedHtmlList;
}

// ----------------------------
// Main Script Execution
// ----------------------------

try
{
    // Retrieve the first selected measure
    var selectedMeasure = Selected.Measures.FirstOrDefault();

    // Check if a measure is selected
    if (selectedMeasure == null)
    {
        ShowError("No measure is selected. Please select a measure.");
        return; // Exit if no measure is selected
    }

    // Construct the DAX expression
    var dax = $"{selectedMeasure.Name} :=\n{selectedMeasure.Expression}";

    // URL of the DAX formatter service
    var url = "https://www.daxformatter.com/";

    // Prepare the POST data with URL encoding
    var postData = $"fx={HttpUtility.UrlEncode(dax)}&embed=1";
    var data = Encoding.ASCII.GetBytes(postData);

    // Create and configure the HTTP web request
    var request = (HttpWebRequest)WebRequest.Create(url);
    request.Method = "POST";
    request.ContentType = "application/x-www-form-urlencoded";
    request.ContentLength = data.Length;

    // Write the POST data to the request stream
    using (var stream = request.GetRequestStream())
    {
        stream.Write(data, 0, data.Length);
    }

    // Get the response from the server
    string responseString;
    using (var response = (HttpWebResponse)request.GetResponse())
    using (var reader = new StreamReader(response.GetResponseStream()))
    {
        responseString = reader.ReadToEnd();
    }

    // Extract the formatted HTML using regex
    var formattedHtmlList = ExtractFormattedHtml(responseString);

    // Combine the extracted HTML segments
    var formattedHtml = string.Join(" ", formattedHtmlList);

    // Set the formatted HTML to the clipboard
    Clipboard.SetText(formattedHtml);

}
catch (WebException webEx)
{
    // Handle web-related exceptions
    ShowError($"An error occurred while contacting the formatter service: {webEx.Message}");
}
catch (Exception ex)
{
    // Handle all other exceptions
    ShowError($"An unexpected error occurred: {ex.Message}");
}
