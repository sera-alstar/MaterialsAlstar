// ========================================================
// HubSpot CRM Frontend Interactions for MaterialsAlstar
// ========================================================

document.addEventListener("DOMContentLoaded", () => {
    // Elements
    const searchInput = document.getElementById("hsSearchInput");
    const tableRows = document.querySelectorAll(".hs-row");
    const selectAllCheckbox = document.getElementById("hsSelectAll");
    const rowCheckboxes = document.querySelectorAll(".hs-row-checkbox");
    const bulkActionsBar = document.getElementById("hsBulkActions");
    const selectedCountSpan = document.getElementById("hsSelectedCount");
    const deselectAllBtn = document.getElementById("hsDeselectAll");
    const visibleCountSpan = document.getElementById("hsVisibleCount");
    const footerCountSpan = document.getElementById("hsFooterCount");
    const resetFiltersBtn = document.getElementById("hsResetFilters");
    const exportCsvBtn = document.getElementById("exportCsvBtn");

    // Drawer Elements
    const drawer = document.getElementById("hsDrawer");
    const drawerBackdrop = document.getElementById("hsDrawerBackdrop");
    const closeDrawerBtn = document.getElementById("hsCloseDrawer");
    const drawerCloseBtn = document.getElementById("drawerCloseBtn");

    // Current Filter State
    let activeView = "all";
    let filterJob = "";
    let filterVendor = "";
    let filterBin = "";
    let filterHeat = "";

    // ----------------------------------------------------
    // 1. HubSpot Slide-out Drawer (Side Panel)
    // ----------------------------------------------------
    function openDrawer(row) {
        if (!row || !drawer || !drawerBackdrop) return;

        const data = row.dataset;
        document.getElementById("drawerJobBadge").textContent = `Job ${data.job || "N/A"}`;
        document.getElementById("drawerTitle").textContent = data.desc || "Material Item";
        document.getElementById("drawerBOMID").textContent = `BOMID: ${data.bomid || "N/A"}`;
        
        document.getElementById("drawerHeat").textContent = data.heat || "-";
        document.getElementById("drawerBin").textContent = data.bin || "-";
        document.getElementById("drawerQty").textContent = `${data.qtyRecv || 0} ${data.unit || "EA"}`;

        document.getElementById("drawerDesc").textContent = data.desc || "-";
        document.getElementById("drawerBomIdVal").textContent = data.bomid || "-";
        document.getElementById("drawerJob").textContent = data.job || "-";
        document.getElementById("drawerPo").textContent = data.po || "-";
        document.getElementById("drawerReceiver").textContent = data.receiver || "-";
        document.getElementById("drawerVendor").textContent = data.vendor || "-";

        document.getElementById("drawerHeatFull").textContent = `🔥 ${data.heat || "None"}`;
        document.getElementById("drawerBinFull").textContent = `📦 ${data.bin || "Unassigned"}`;
        document.getElementById("drawerStatus").textContent = `● ${data.status || "Received"}`;
        document.getElementById("drawerRecvBy").textContent = data.recvBy || "-";
        document.getElementById("drawerRecvDate").textContent = data.recvDate || "-";

        drawer.classList.add("active");
        drawerBackdrop.classList.add("active");
    }

    function closeDrawer() {
        if (drawer) drawer.classList.remove("active");
        if (drawerBackdrop) drawerBackdrop.classList.remove("active");
    }

    // Attach click listeners to rows & preview buttons
    tableRows.forEach(row => {
        row.addEventListener("click", (e) => {
            // Don't open if clicked directly on a checkbox
            if (e.target.type === "checkbox") return;
            openDrawer(row);
        });
    });

    closeDrawerBtn?.addEventListener("click", closeDrawer);
    drawerCloseBtn?.addEventListener("click", closeDrawer);
    drawerBackdrop?.addEventListener("click", closeDrawer);

    // ----------------------------------------------------
    // 2. Checkboxes & Floating Bulk Actions
    // ----------------------------------------------------
    function updateSelectionState() {
        let count = 0;
        rowCheckboxes.forEach(cb => {
            const tr = cb.closest("tr");
            if (cb.checked) {
                count++;
                tr?.classList.add("selected");
            } else {
                tr?.classList.remove("selected");
            }
        });

        if (selectedCountSpan) selectedCountSpan.textContent = count;
        if (bulkActionsBar) {
            if (count > 0) {
                bulkActionsBar.classList.add("active");
            } else {
                bulkActionsBar.classList.remove("active");
            }
        }

        if (selectAllCheckbox) {
            selectAllCheckbox.checked = count > 0 && count === rowCheckboxes.length;
            selectAllCheckbox.indeterminate = count > 0 && count < rowCheckboxes.length;
        }
    }

    selectAllCheckbox?.addEventListener("change", () => {
        const isChecked = selectAllCheckbox.checked;
        rowCheckboxes.forEach(cb => {
            // Only select visible rows
            const tr = cb.closest("tr");
            if (tr && tr.style.display !== "none") {
                cb.checked = isChecked;
            }
        });
        updateSelectionState();
    });

    rowCheckboxes.forEach(cb => {
        cb.addEventListener("change", updateSelectionState);
    });

    deselectAllBtn?.addEventListener("click", () => {
        rowCheckboxes.forEach(cb => cb.checked = false);
        if (selectAllCheckbox) selectAllCheckbox.checked = false;
        updateSelectionState();
    });

    // ----------------------------------------------------
    // 3. HubSpot Saved View Tabs
    // ----------------------------------------------------
    const viewTabs = document.querySelectorAll(".hs-view-tab");
    viewTabs.forEach(tab => {
        tab.addEventListener("click", () => {
            viewTabs.forEach(t => t.classList.remove("active"));
            tab.classList.add("active");
            activeView = tab.dataset.view || "all";
            applyAllFilters();
        });
    });

    // ----------------------------------------------------
    // 4. Multi-Filter & Search Engine
    // ----------------------------------------------------
    function applyAllFilters() {
        const query = (searchInput?.value || "").toLowerCase().trim();
        let visibleCount = 0;

        tableRows.forEach(row => {
            const data = row.dataset;
            const job = data.job || "";
            const po = (data.po || "").toLowerCase();
            const receiver = (data.receiver || "").toLowerCase();
            const vendor = data.vendor || "";
            const desc = (data.desc || "").toLowerCase();
            const bomid = (data.bomid || "").toLowerCase();
            const heat = data.heat || "";
            const bin = data.bin || "";

            // 1. Saved View Check
            let matchesView = true;
            if (activeView !== "all" && job !== activeView) {
                matchesView = false;
            }

            // 2. Chip Filters Check
            const matchesJob = !filterJob || job === filterJob;
            const matchesVendor = !filterVendor || vendor === filterVendor;
            const matchesBin = !filterBin || bin === filterBin;
            const matchesHeat = !filterHeat || heat === filterHeat;

            // 3. Text Query Search across properties
            const matchesQuery = !query ||
                job.toLowerCase().includes(query) ||
                po.includes(query) ||
                receiver.includes(query) ||
                vendor.toLowerCase().includes(query) ||
                desc.includes(query) ||
                bomid.includes(query) ||
                heat.toLowerCase().includes(query) ||
                bin.toLowerCase().includes(query);

            if (matchesView && matchesJob && matchesVendor && matchesBin && matchesHeat && matchesQuery) {
                row.style.display = "";
                visibleCount++;
            } else {
                row.style.display = "none";
            }
        });

        if (visibleCountSpan) visibleCountSpan.textContent = visibleCount;
        if (footerCountSpan) footerCountSpan.textContent = visibleCount;
        updateSelectionState();
    }

    searchInput?.addEventListener("input", applyAllFilters);

    // Dropdown filters handling
    function setupDropdownFilter(menuId, labelId, btnId, onSelect) {
        const menu = document.getElementById(menuId);
        const label = document.getElementById(labelId);
        const btn = document.getElementById(btnId);

        if (!menu || !label || !btn) return;

        menu.querySelectorAll("a").forEach(item => {
            item.addEventListener("click", (e) => {
                e.preventDefault();
                menu.querySelectorAll("a").forEach(a => a.classList.remove("active"));
                item.classList.add("active");

                const val = item.dataset.val || "";
                label.textContent = val ? item.textContent.replace("🔥 ", "").replace("Bin ", "").replace("Job ", "") : "All";
                
                if (val) {
                    btn.classList.add("active");
                } else {
                    btn.classList.remove("active");
                }

                onSelect(val);
                applyAllFilters();
            });
        });
    }

    setupDropdownFilter("jobDropdownMenu", "jobFilterLabel", "jobFilterBtn", (val) => filterJob = val);
    setupDropdownFilter("vendorDropdownMenu", "vendorFilterLabel", "vendorFilterBtn", (val) => filterVendor = val);
    setupDropdownFilter("binDropdownMenu", "binFilterLabel", "binFilterBtn", (val) => filterBin = val);
    setupDropdownFilter("heatDropdownMenu", "heatFilterLabel", "heatFilterBtn", (val) => filterHeat = val);

    // Reset Filters
    resetFiltersBtn?.addEventListener("click", () => {
        if (searchInput) searchInput.value = "";
        filterJob = "";
        filterVendor = "";
        filterBin = "";
        filterHeat = "";

        document.getElementById("jobFilterLabel").textContent = "All";
        document.getElementById("vendorFilterLabel").textContent = "All";
        document.getElementById("binFilterLabel").textContent = "All";
        document.getElementById("heatFilterLabel").textContent = "All";

        document.querySelectorAll(".hs-filter-btn").forEach(btn => btn.classList.remove("active"));
        document.querySelectorAll(".dropdown-menu a").forEach(a => a.classList.remove("active"));
        document.querySelectorAll(".dropdown-menu li:first-child a").forEach(a => a.classList.add("active"));

        viewTabs[0]?.click();
        applyAllFilters();
    });

    // ----------------------------------------------------
    // 5. Export to CSV (HubSpot Action)
    // ----------------------------------------------------
    exportCsvBtn?.addEventListener("click", (e) => {
        e.preventDefault();
        let csv = "Job #,PO #,Receiver ID,BOMID,Description,Vendor,Heat #,Bin Location,Qty Shipped,Qty Received,Unit,Status\n";

        tableRows.forEach(row => {
            if (row.style.display !== "none") {
                const d = row.dataset;
                csv += `"${d.job}","${d.po}","${d.receiver}","${d.bomid}","${d.desc}","${d.vendor}","${d.heat}","${d.bin}","${d.qtyShip}","${d.qtyRecv}","${d.unit}","${d.status}"\n`;
            }
        });

        const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.setAttribute("href", url);
        link.setAttribute("download", `MaterialsAlstar_Export_${new Date().toISOString().slice(0, 10)}.csv`);
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    });
});
