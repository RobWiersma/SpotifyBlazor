window.devicePicker = {
    registerOutsideClick: function (dotnetRef) {
        document.addEventListener("mousedown", function () {
            dotnetRef.invokeMethodAsync("CloseMenu");
        });
    }
};

window.volumePopup = {
    registerOutsideClick: function (dotnetRef) {
        document.addEventListener("mousedown", function () {
            dotnetRef.invokeMethodAsync("CloseVolume");
        });
    }
};
