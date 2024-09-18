//Add Descriptions to All Measures using comment lines of an expresssion

//for all measures in the model
foreach (var m in Model.AllMeasures)
{
    var expr = m.Expression; //measure expression
    
    var desc = "";
    var i = 0;

    //for all lines of the expression
    foreach (string line in expr.Split('\n'))
    {
    if (i < 5)         
        {
            desc = desc + line + "\n"; //add line to the description
        }

    if (i == 5 ) { desc = desc + "..."; }
    i=i+1;
    }

    m.Description = desc; //update measure escription

    
}
