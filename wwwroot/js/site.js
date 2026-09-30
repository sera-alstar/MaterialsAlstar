// MaterialsAlstar Client-Side QC Search & Filtering
document.addEventListener("DOMContentLoaded", () => {
    const searchInput = document.getElementById("searchInput");
    const jobFilter = document.getElementById("jobFilter");
    const binFilter = document.getElementById("binFilter");
    const resetButton = document.getElementById("resetFilters");
    const tableRows = document.querySelectorAll(".material-row");
    const countDisplay = document.getElementById("visibleCount");

    if (!tableRows.length) return;

    function applyFilters() {
        const query = (searchInput?.value || "").toLowerCase().trim();
        const selectedJob = (jobFilter?.value || "").trim();
        const selectedBin = (binFilter?.value || "").trim();

        let visibleCount = 0;

        tableRows.forEach(row => {
            const job = row.dataset.job || "";
            const po = (row.dataset.po || "").toLowerCase();
            const receiver = (row.dataset.receiver || "").toLowerCase();
            const bomid = (row.dataset.bomid || "").toLowerCase();
            const desc = (row.dataset.desc || "").toLowerCase();
            const vendor = (row.dataset.vendor || "").toLowerCase();
            const heat = (row.dataset.heat || "").toLowerCase();
            const bin = row.dataset.bin || "";

            // Check dropdown filters
            const matchesJob = !selectedJob || job === selectedJob;
            const matchesBin = !selectedBin || bin === selectedBin;

            // Check search text across all relevant fields
            const matchesQuery = !query || 
                job.toLowerCase().includes(query) ||
                po.includes(query) ||
                receiver.includes(query) ||
                bomid.includes(query) ||
                desc.includes(query) ||
                vendor.includes(query) ||
                heat.includes(query) ||
                bin.toLowerCase().includes(query);

            if (matchesJob && matchesBin && matchesQuery) {
                row.style.display = "";
                visibleCount++;
            } else {
                row.style.display = "none";
            }
        });

        if (countDisplay) {
            countDisplay.textContent = visibleCount;
        }
    }

    searchInput?.addEventListener("input", applyFilters);
    jobFilter?.addEventListener("change", applyFilters);
    binFilter?.addEventListener("change", applyFilters);

    resetButton?.addEventListener("click", () => {
        if (searchInput) searchInput.value = "";
        if (jobFilter) jobFilter.value = "";
        if (binFilter) binFilter.value = "";
        applyFilters();
    });
});
