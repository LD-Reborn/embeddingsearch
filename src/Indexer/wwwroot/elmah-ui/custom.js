document.addEventListener('DOMContentLoaded', async () => {
    const url = new URL(window.location.href);
    const btn = document.createElement("a");
    btn.href = url.searchParams.get('ReturnUrl') ?? "/";
    btn.innerText = "⎋";
    btn.setAttribute("aria-label", "Return to Front-End");
    btn.className = "elmah-return-btn";

    document.body.appendChild(btn);

    const showLabelBriefly = () => {
        btn.classList.add("show-label");
        setTimeout(() => btn.classList.remove("show-label"), 2000);
    };

    setTimeout(showLabelBriefly, 1000);
});
