// "Copy link" button: copies the full address of the page named in data-copy-path.
document.addEventListener("click", async (event) => {
  const button = event.target.closest("[data-copy-path]");
  if (!button) return;

  const link = window.location.origin + button.dataset.copyPath;
  const label = button.textContent;

  try {
    await navigator.clipboard.writeText(link);
  } catch {
    // The clipboard API only works on localhost or HTTPS. Fall back to selecting the text and copying it.
    const box = document.createElement("textarea");
    box.value = link;
    document.body.appendChild(box);
    box.select();
    document.execCommand("copy");
    box.remove();
  }

  button.textContent = "Copied!";
  setTimeout(() => { button.textContent = label; }, 1500);
});
