// Marks intentional full-page exits so the Interactive Server reconnect modal stays hidden while the tab unloads.
(function () {
    document.addEventListener(
        'click',
        function (e) {
            if (e.defaultPrevented || e.button !== 0) {
                return;
            }
            if (e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) {
                return;
            }
            const anchor = e.target.closest('a');
            if (!anchor || !anchor.classList.contains('hub-back-btn')) {
                return;
            }
            const href = anchor.getAttribute('href');
            if (!href || href === '#' || href.startsWith('javascript:')) {
                return;
            }
            document.documentElement.classList.add('blazor-exit-navigation');
        },
        true
    );
})();
