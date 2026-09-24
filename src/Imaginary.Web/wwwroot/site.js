// Imaginary Web Client Scripts

(function () {
    // Initialize theme immediately to prevent flashing
    const savedTheme = localStorage.getItem('imaginary-theme') || 'dark';
    document.documentElement.setAttribute('data-theme', savedTheme);
})();

window.ImaginarySite = {
    getTheme: function () {
        return localStorage.getItem('imaginary-theme') || 'dark';
    },

    toggleTheme: function () {
        const current = document.documentElement.getAttribute('data-theme') || 'dark';
        const next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('imaginary-theme', next);
        return next;
    },

    triggerLogoBurst: function () {
        const logo = document.getElementById('hero-interactive-logo');
        if (!logo) return;

        logo.classList.remove('logo-click-burst');
        // Force reflow
        void logo.offsetWidth;
        logo.classList.add('logo-click-burst');

        // Create particle burst effect
        const container = logo.parentElement;
        if (container) {
            for (let i = 0; i < 12; i++) {
                const particle = document.createElement('span');
                particle.className = 'logo-particle';
                const angle = (i / 12) * 360;
                const distance = 60 + Math.random() * 40;
                particle.style.setProperty('--angle', angle + 'deg');
                particle.style.setProperty('--distance', distance + 'px');
                particle.style.left = '50%';
                particle.style.top = '50%';
                container.appendChild(particle);
                setTimeout(() => particle.remove(), 800);
            }
        }

        setTimeout(() => {
            logo.classList.remove('logo-click-burst');
        }, 850);
    },

    initScrollAnimations: function () {
        const sections = document.querySelectorAll('.anim-on-scroll');
        if (!sections.length) return;

        const observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.classList.add('anim-visible');
                    // Stagger children with glass-card or stat-counter-card
                    const cards = entry.target.querySelectorAll('.glass-card, .stat-counter-card, .screenshot-tab-btn');
                    cards.forEach((card, idx) => {
                        card.style.transitionDelay = (idx * 0.08) + 's';
                        card.classList.add('anim-child-visible');
                    });
                    observer.unobserve(entry.target);
                }
            });
        }, {
            threshold: 0.1,
            rootMargin: '0px 0px -40px 0px'
        });

        sections.forEach(section => observer.observe(section));
    }
};
