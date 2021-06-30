function getTelerikGridItems(id) {
    var grid = $telerik.findGrid(id);
    var tableView = grid.get_masterTableView();
    var columns = tableView.get_columns().map(function (column) {
        return {
            field: column.get_dataField(),
            uniqueName: column.get_uniqueName(),
            displayName: column.get_element().textContent.trim()
        }
    });
    var output = tableView.get_dataItems().map(function(row) {
        var outputRow = {};
        for (var c in columns) {
            if (columns[c].displayName === '') continue;
            var cell = row.get_cell(columns[c].uniqueName);
            var checkbox = cell.querySelector('input[type="checkbox"]');
            outputRow[columns[c].displayName] = checkbox !== null
                ? outputRow[columns[c].displayName] = checkbox.checked
                : outputRow[columns[c].displayName] = cell.textContent.trim();
        }
        return outputRow;
    });
    return output;
}