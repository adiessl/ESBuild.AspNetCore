var widgetLibrary;
(function (widgetLibrary) {
    function getMessage() {
        return "Widget ready";
    }
    widgetLibrary.getMessage = getMessage;
})(widgetLibrary || (widgetLibrary = {}));

function initWidget(elementId) {
    document.getElementById(elementId).textContent = widgetLibrary.getMessage();
}
