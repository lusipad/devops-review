(function initializeContentScript() {
  "use strict";

  if (globalThis.__devOpsReviewContentLoaded) {
    return;
  }
  globalThis.__devOpsReviewContentLoaded = true;

  const shared = globalThis.DevOpsReviewShared;
  let currentSelection = null;
  let buttonHost = null;

  document.addEventListener("selectionchange", () => {
    if (!document.getSelection()?.toString().trim()) {
      hideButton();
    }
  });
  document.addEventListener("mouseup", () => void captureSelection());
  document.addEventListener("keyup", (event) => {
    if (event.key === "Shift" || event.shiftKey) {
      void captureSelection();
    }
  });

  async function captureSelection() {
    const selection = document.getSelection();
    const { serverUrls = [] } = await chrome.storage.local.get("serverUrls");
    currentSelection = shared.buildReviewSelection(selection, document, serverUrls);
    if (!currentSelection) {
      hideButton();
      return;
    }

    const range = selection.getRangeAt(0);
    const rect = range.getBoundingClientRect();
    showButton(rect);
  }

  function showButton(rect) {
    if (!buttonHost) {
      buttonHost = document.createElement("div");
      buttonHost.id = "devops-review-ask-codex-host";
      buttonHost.style.position = "fixed";
      buttonHost.style.zIndex = "2147483647";
      const shadow = buttonHost.attachShadow({ mode: "closed" });
      const button = document.createElement("button");
      button.type = "button";
      button.textContent = "问 Codex";
      Object.assign(button.style, {
        all: "initial",
        boxSizing: "border-box",
        display: "inline-flex",
        alignItems: "center",
        minHeight: "34px",
        padding: "7px 12px",
        border: "1px solid rgba(255,255,255,.18)",
        borderRadius: "8px",
        background: "#101828",
        color: "#ffffff",
        boxShadow: "0 8px 24px rgba(16,24,40,.25)",
        cursor: "pointer",
        font: "600 13px/1.2 -apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif",
      });
      button.addEventListener("click", async () => {
        if (!currentSelection) {
          return;
        }
        await chrome.runtime.sendMessage({
          type: "selection.captured",
          payload: currentSelection,
        });
        hideButton();
      });
      shadow.append(button);
      document.documentElement.append(buttonHost);
    }

    const left = Math.max(8, Math.min(window.innerWidth - 110, rect.right + 8));
    const top = Math.max(8, Math.min(window.innerHeight - 50, rect.bottom + 8));
    buttonHost.style.left = `${left}px`;
    buttonHost.style.top = `${top}px`;
    buttonHost.hidden = false;
  }

  function hideButton() {
    currentSelection = null;
    if (buttonHost) {
      buttonHost.hidden = true;
    }
  }
})();
