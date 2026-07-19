(function initializeOptions() {
  "use strict";

  const shared = globalThis.DevOpsReviewShared;
  const form = document.querySelector("#settings-form");
  const input = document.querySelector("#server-url");
  const status = document.querySelector("#status");

  void load();
  form.addEventListener("submit", save);

  async function load() {
    const { serverUrls = [] } = await chrome.storage.local.get("serverUrls");
    input.value = serverUrls[0] || "";
  }

  async function save(event) {
    event.preventDefault();
    status.textContent = "";
    try {
      const serverUrl = shared.normalizeServerUrl(input.value);
      const pattern = shared.permissionPattern(serverUrl);
      const granted = await chrome.permissions.request({ origins: [pattern] });
      if (!granted) {
        status.textContent = "未授予站点权限，设置没有保存。";
        return;
      }

      await chrome.storage.local.set({ serverUrls: [serverUrl] });
      try {
        await chrome.scripting.unregisterContentScripts({ ids: [shared.CONTENT_SCRIPT_ID] });
      } catch {
        // The script is not registered on a fresh install.
      }
      await chrome.scripting.registerContentScripts([
        {
          id: shared.CONTENT_SCRIPT_ID,
          matches: [pattern],
          js: ["shared.js", "content.js"],
          runAt: "document_idle",
          persistAcrossSessions: true,
        },
      ]);

      input.value = serverUrl;
      status.textContent = "已保存。重新加载已打开的 Azure DevOps 页面后即可划选代码。";
    } catch (error) {
      status.textContent = `保存失败：${error.message}`;
    }
  }
})();
