// IndexedDB backing for Playbook's data directory (see BrowserDataStore.cs).
const DB_NAME = "playbook";
const STORE = "files";

let dbPromise;
function db() {
  dbPromise ??= new Promise((resolve, reject) => {
    const request = indexedDB.open(DB_NAME, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
  return dbPromise;
}

async function run(mode, action) {
  const database = await db();
  return new Promise((resolve, reject) => {
    const tx = database.transaction(STORE, mode);
    const request = action(tx.objectStore(STORE));
    tx.oncomplete = () => resolve(request?.result);
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
  });
}

export const keys = async () => (await run("readonly", (store) => store.getAllKeys())).map(String);
export const read = async (key) => new Uint8Array((await run("readonly", (store) => store.get(key))) ?? []);
export const write = (key, bytes) => run("readwrite", (store) => store.put(bytes, key));
export const remove = (key) => run("readwrite", (store) => store.delete(key));
