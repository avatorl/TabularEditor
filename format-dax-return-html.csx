//Format DAX code and return HTML code (for blog posts)

using System.Net;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Web;

if (Selected.Tables.Count() == 1) {

//for all selected measures
foreach (var t in Selected.CalculatedTables) {

    var dax = t.Name + " =\n" + t.Expression;

    var url = "https://www.daxformatter.com/";

    var request = (HttpWebRequest)WebRequest.Create(url);
    var postData = "fx=" + HttpUtility.UrlEncode(dax);
        postData += "&embed=1";
    var data = Encoding.ASCII.GetBytes(postData);

    request.Method = "POST";
    request.ContentType = "application/x-www-form-urlencoded";
    request.ContentLength = data.Length;

    using (var stream = request.GetRequestStream())
    {
        stream.Write(data, 0, data.Length);
    }

    var response = (HttpWebResponse)request.GetResponse();

    var responseString = new StreamReader(response.GetResponseStream()).ReadToEnd();

    List<string> destList = new List<string>();

    foreach (Match match in Regex.Matches(responseString, "<div.*?><div.*?>(.*?)</div></div>"))
    destList.Add(match.Groups[1].Value);

    Clipboard.SetText (String.Join(" ", destList)); 

}
}
