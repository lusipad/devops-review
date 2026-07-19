"use strict";

chrome.runtime.onInstalled.addListener(async (details) => {
  await chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: true });
  if (details.reason === "install") {
    const { serverUrls = [] } = await chrome.storage.local.get("serverUrls");
    if (serverUrls.length === 0) {
      await chrome.runtime.openOptionsPage();
    }
  }
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type !== "selection.captured" || !sender.tab?.id) {
    return false;
  }

  (async () => {
    await chrome.storage.session.set({ activeSelection: message.payload });
    await chrome.sidePanel.open({ tabId: sender.tab.id });
    sendResponse({ opened: true });
  })().catch((error) => {
    sendResponse({ opened: false, message: error.message });
  });
  return true;
});
