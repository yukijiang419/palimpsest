mergeInto(LibraryManager.library, {
  $PalimpsestStore: function(target, token, write, data, previous) {
    var finished = false, database = null, transaction = null;
    var timer = setTimeout(function() {
      if (transaction) { try { transaction.abort(); } catch (_) {} }
      reply({error: 'Storage timed out. Retry saving.'});
    }, 12000);
    function reply(result) {
      if (finished) return;
      finished = true;
      clearTimeout(timer);
      if (database) database.close();
      result.request = token;
      SendMessage(target, 'OnStorageReply', JSON.stringify(result));
    }
    try {
      var open = indexedDB.open('Palimpsest.Save', 1);
      open.onupgradeneeded = function() { open.result.createObjectStore('saves'); };
      open.onerror = function() { reply({error: String(open.error)}); };
      open.onblocked = function() { reply({error: 'Storage is blocked by another page.'}); };
      open.onsuccess = function() {
        database = open.result;
        // An open request cannot be cancelled. A late success must never perform a stale write.
        if (finished) { database.close(); return; }
        try {
          transaction = database.transaction('saves', write ? 'readwrite' : 'readonly');
          var store = transaction.objectStore('saves');
          var current, backup;
          if (write) {
            store.put(data, 'current');
            store.put(previous, 'backup');
          } else {
            current = store.get('current');
            backup = store.get('backup');
          }
          transaction.oncomplete = function() {
            reply(write ? {} : {current: current.result || '', backup: backup.result || ''});
          };
          transaction.onabort = function() { reply({error: String(transaction.error || 'Storage transaction aborted.')}); };
        } catch (error) {
          if (transaction) { try { transaction.abort(); } catch (_) {} }
          reply({error: String(error)});
        }
      };
    } catch (error) { reply({error: String(error)}); }
  },
  PalimpsestSaveRead__deps: ['$PalimpsestStore'],
  PalimpsestSaveRead: function(receiver, request) {
    var target = UTF8ToString(receiver);
    PalimpsestStore(target, UTF8ToString(request), false, '', '');
    if (!Module.palimpsestVisibility) {
      Module.palimpsestVisibility = true;
      document.addEventListener('visibilitychange', function() {
        if (document.hidden) SendMessage(target, 'OnPageHidden', '');
      });
    }
  },
  PalimpsestSaveWrite__deps: ['$PalimpsestStore'],
  PalimpsestSaveWrite: function(receiver, request, current, backup) {
    PalimpsestStore(UTF8ToString(receiver), UTF8ToString(request), true, UTF8ToString(current), UTF8ToString(backup));
  }
});
