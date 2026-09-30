// MaterialsAlstar Client-Side JavaScript
document.addEventListener("DOMContentLoaded", () => {
    const searchInput = document.getElementById("materialSearch");
    const tableRows = document.querySelectorAll(".material-row");
    const countDisplay = document.getElementById("visibleCount");

    if (!searchInput || tableRows.length === 0) return;

    searchInput.addEventListener("input", () => {
        const query = searchInput.value.toLowerCase().trim();
        let visibleCount = 0;

        tableRows.forEach(row => {
            const name = row.querySelector(".material-name")?.textContent.toLowerCase() || "";
            const sku = row.querySelector(".material-sku")?.textContent.toLowerCase() || "";
            const category = row.querySelector(".material-category")?.textContent.toLowerCase() || "";

            const matches = name.includes(query) || sku.includes(query) || category.includes(query);

            if (matches) {
                row.style.display = "";
                visibleCount++;
            } else {
                row.style.display = "none";
            }
        });

        if (countDisplay) {
            countDisplay.textContent = visibleCount;
        }
    });
});
