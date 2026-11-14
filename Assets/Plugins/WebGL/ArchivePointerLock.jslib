mergeInto(LibraryManager.library, {
  ArchiveSetPointerLockDesired: function (locked) {
    var canvas = Module.canvas;
    canvas.archivePointerLockWanted = !!locked;
    if (canvas.archivePointerLockGuard || !canvas.requestPointerLock) return;
    canvas.archivePointerLockGuard = true;
    var request = canvas.requestPointerLock.bind(canvas);
    var pending = false;
    var rejected = function (error) {
      pending = false;
      // Denial during focus changes is recoverable on the next canvas click.
      if (error && ["WrongDocumentError", "NotAllowedError", "SecurityError",
          "NotSupportedError", "AbortError"].indexOf(error.name) < 0)
        console.warn("Pointer lock request failed: " + error);
    };
    canvas.requestPointerLock = function () {
      if (!canvas.archivePointerLockWanted || pending || !canvas.isConnected ||
          canvas.ownerDocument !== document || !document.hasFocus() ||
          document.visibilityState === "hidden" || document.pointerLockElement === canvas) return;
      try {
        var result = request();
        if (result && typeof result.then === "function") {
          pending = true;
          result.then(function () {
            pending = false;
            if (!canvas.archivePointerLockWanted && document.pointerLockElement === canvas)
              document.exitPointerLock();
          }, rejected);
        }
      } catch (error) { rejected(error); }
    };
    canvas.addEventListener("mousedown", function () {
      if (canvas.archivePointerLockWanted) canvas.requestPointerLock();
    });
  }
});
