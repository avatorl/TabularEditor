//Add Descriptions to All Measures using comment lines of an expresssion

//for all measures in the model
foreach (var m in Model.AllMeasures)
{
    var expr = m.Expression; //measure expression
    
    var desc = "";

    //for all lines of the expression
    foreach (string line in expr.Split('\n'))
    {
        //if there are 2+ characters in the line
        if (line.Length >= 2) {
            //if line is a comment starting with //
            if ( line.Substring(0, 2) == "//" )
            {
                desc = desc + line + "\n"; //add line to the description
            }
        }
    }

    m.Description = desc; //update measure escription
    
}
