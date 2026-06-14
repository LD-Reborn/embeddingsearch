document.addEventListener('DOMContentLoaded', async () => {
    const url = new URL(window.location.href);
    const btn = document.createElement("a");
    btn.href = url.searchParams.get('ReturnUrl') ?? "/";
    btn.innerText = "⎋";
    btn.setAttribute("aria-label", "Return to Front-End");
    btn.className = "swagger-return-btn";

    document.body.appendChild(btn);

    const togglePosition = () => {
        if (window.scrollY > 0) {
            btn.classList.add("scrolled");
        } else {
            btn.classList.remove("scrolled");
        }
    };

    // Initial state
    togglePosition();

    // On scroll
    window.addEventListener("scroll", togglePosition, { passive: true });
});
