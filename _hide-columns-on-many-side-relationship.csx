//https://github.com/TabularEditor/Scripts/blob/master/Basic/Hide%20columns%20on%20the%20many%20side%20of%20a%20relationship.csx

/*
 * Title: Hide columns on the many side of a relationship  
 *
 * Author: Matt Allington, https://exceleratorbi.com.au  
 *
 * it is dangerous to use columns on the many side of a relationship as it can 
 * produce unexpected results, so it is a best practice to hide these columns
 * to discourage their use in reports.
 */

// Hide all columns on many side of a join
foreach (var r in Model.Relationships)
{ // hide all columns on the many side of a join
    var c = r.FromColumn.Name;
    var t = r.FromTable.Name;
    Model.Tables[t].Columns[c].IsHidden = true;
}
