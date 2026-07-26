/**
 * Parola onerisi + guc gostergesi (ChangeCredentials vb.).
 * Kural: buyuk harf + kucuk harf + rakam; uzunluk data-min-length'ten.
 */
(() => {
    const lowers = "abcdefghijkmnopqrstuvwxyz";
    const uppers = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    const digits = "23456789";
    const symbols = "!@#$%&*-_+=?";

    function pick(alphabet, count, random) {
        let out = "";
        for (let i = 0; i < count; i++) {
            out += alphabet[random() % alphabet.length];
        }
        return out;
    }

    function shuffle(chars, random) {
        const arr = chars.split("");
        for (let i = arr.length - 1; i > 0; i--) {
            const j = random() % (i + 1);
            [arr[i], arr[j]] = [arr[j], arr[i]];
        }
        return arr.join("");
    }

    function secureRandomInt() {
        const buf = new Uint32Array(1);
        crypto.getRandomValues(buf);
        return buf[0];
    }

    function generatePassword(minLength) {
        const length = Math.max(minLength, 16);
        const random = secureRandomInt;
        // En az birer zorunlu karakter, kalan karisik (sembol dahil → guclu).
        let password =
            pick(uppers, 1, random) +
            pick(lowers, 1, random) +
            pick(digits, 1, random) +
            pick(symbols, 1, random) +
            pick(uppers + lowers + digits + symbols, length - 4, random);
        return shuffle(password, random);
    }

    /**
     * 0 = bos, 1 = zayif, 2 = orta, 3 = guclu
     */
    function scorePassword(value, minLength) {
        if (!value) {
            return 0;
        }

        let score = 0;
        if (value.length >= minLength) score++;
        if (value.length >= Math.max(minLength + 4, 12)) score++;
        if (/[a-z]/.test(value) && /[A-Z]/.test(value)) score++;
        if (/[0-9]/.test(value)) score++;
        if (/[^A-Za-z0-9]/.test(value)) score++;

        if (score <= 2) return 1;
        if (score <= 4) return 2;
        return 3;
    }

    function bind(root) {
        const password = root.querySelector("[data-password-input]");
        const form = root.closest("form") || root;
        const confirm = form.querySelector("[data-password-confirm]");
        const suggest = root.querySelector("[data-password-suggest]");
        const meter = root.querySelector("[data-password-meter]");
        const label = root.querySelector("[data-password-strength-label]");
        const minLength = Number(root.dataset.minLength || "8");

        const labels = {
            0: "",
            1: root.dataset.labelWeak || "Weak",
            2: root.dataset.labelMedium || "Medium",
            3: root.dataset.labelStrong || "Strong",
        };

        const classes = {
            0: "",
            1: "password-strength--weak",
            2: "password-strength--medium",
            3: "password-strength--strong",
        };

        function refresh() {
            const level = scorePassword(password.value, minLength);
            if (meter) {
                meter.dataset.level = String(level);
                meter.setAttribute("aria-valuenow", String(level));
                meter.querySelectorAll("[data-strength-bar]").forEach((bar, index) => {
                    const active = level > index;
                    bar.classList.toggle("is-active", active);
                    bar.classList.remove(
                        "password-strength--weak",
                        "password-strength--medium",
                        "password-strength--strong");
                    if (active) {
                        bar.classList.add(classes[level]);
                    }
                });
            }
            if (label) {
                label.textContent = labels[level] || "";
                label.className = "password-strength-label small " + (classes[level] || "text-secondary");
            }
        }

        if (suggest && password) {
            suggest.addEventListener("click", () => {
                const generated = generatePassword(minLength);
                password.value = generated;
                password.type = "text";
                if (confirm) {
                    confirm.value = generated;
                }
                password.dispatchEvent(new Event("input", { bubbles: true }));
                refresh();
                // Kisa sure goster, sonra tekrar gizle.
                window.setTimeout(() => {
                    password.type = "password";
                }, 2500);
            });
        }

        if (password) {
            password.addEventListener("input", refresh);
            refresh();
        }
    }

    document.querySelectorAll("[data-password-field]").forEach(bind);
})();
