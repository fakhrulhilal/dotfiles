function selectTelerikComboBoxItem(id, text) {
    var combobox = $telerik.findComboBox(id);
    var isLoadOnDemandEnabled = combobox.get_enableLoadOnDemand();
    if (isLoadOnDemandEnabled) {
        combobox.set_text(text);
    }
    else {
        var items = combobox.get_items();
        for (var i = 0, total = items.get_count(); i < total; i++) {
            var item = items.getItem(i);
            if (item.get_text() === text) {
                item.select();
                item.set_selected();
                break;
            }
        }
    }
}