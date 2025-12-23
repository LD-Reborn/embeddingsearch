// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
function createToastContainer() {
    const container = document.createElement('div');
    container.id = 'toastContainer';
    container.className = 'toast-container position-fixed bottom-0 end-0 p-3';
    container.setAttribute("aria-live", "polite");
    container.setAttribute("aria-atomic", "true");

    const liveRegion = document.createElement('div');
    liveRegion.id = 'toastLiveRegion';
    liveRegion.className = 'visually-hidden';
    liveRegion.setAttribute('aria-live', 'assertive');
    liveRegion.setAttribute('aria-atomic', 'true');
    container.appendChild(liveRegion);

    document.body.appendChild(container);
    return container;
}

// Simple toast helper
function showToast(message, type) {
    const toastContainer = document.getElementById('toastContainer') || createToastContainer();
    const toast = document.createElement('div');
    toast.className = `toast align-items-center text-white bg-${type} border-0`;
    toast.role = 'alert';
    var useDarkElements = type === "warning"
    toast.innerHTML = `
        <div class="d-flex">
            <div class="toast-body">${message}</div>
            <button type="button" class="btn-close${useDarkElements ? "" : " btn-close-white"} me-2 m-auto"${useDarkElements ? ' style="filter: unset;"' : ""} data-bs-dismiss="toast" aria-label="${window.appTranslations.closeAlert}"></button>
        </div>
    `;
    if (useDarkElements) {
        toast.classList.remove("text-white");
        toast.classList.add("text-dark");
    }
    toastContainer.appendChild(toast);

    const liveRegion = document.getElementById('toastLiveRegion');
    if (liveRegion) {
        liveRegion.textContent = '';
        setTimeout(() => liveRegion.textContent = message, 500);
    }

    const bsToast = new bootstrap.Toast(toast, { delay: 10000 });
    bsToast.show();
    toast.addEventListener('hidden.bs.toast', () => toast.remove());
}