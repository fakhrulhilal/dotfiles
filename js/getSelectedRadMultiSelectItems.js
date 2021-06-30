function getSelectedRadMultiSelectItems(id) {
    var multiSelect = $telerik.findComboBox(id);
    var items = multiSelect.get_selectedDataItems();
    return items.map(function (i) { return i.text; });
}