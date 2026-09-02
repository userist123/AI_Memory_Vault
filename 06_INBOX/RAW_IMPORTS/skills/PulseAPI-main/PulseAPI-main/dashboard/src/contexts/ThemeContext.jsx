import { createContext, useContext, useEffect, useState } from 'react';

const ThemeContext = createContext();

export function useTheme() {
    const context = useContext(ThemeContext);
    if (!context) {
        throw new Error('useTheme must be used within a ThemeProvider');
    }
    return context;
}

const themes = {
    dark: {
        name: 'Warm Blue Dark',
        description: 'Deep warm blue dark mode with glass effects',
        class: 'theme-dark',
        dark: true
    },
    light: {
        name: 'Warm Blue Light',
        description: 'Clean light warm-blue theme for better readability',
        class: 'theme-light',
        dark: false
    }
};

export function ThemeProvider({ children }) {
    const [currentTheme, setCurrentTheme] = useState(() => {
        if (typeof window !== 'undefined') {
            const savedTheme = localStorage.getItem('app-theme');
            if (savedTheme === 'purple') return 'dark';
            return (savedTheme && themes[savedTheme]) ? savedTheme : 'dark';
        }
        return 'dark';
    });

    useEffect(() => {
        const root = document.documentElement;

        Object.values(themes).forEach(theme => {
            root.classList.remove(theme.class);
        });

        root.classList.add(themes[currentTheme].class);

        if (themes[currentTheme].dark) {
            root.classList.add('dark');
        } else {
            root.classList.remove('dark');
        }

        localStorage.setItem('app-theme', currentTheme);
    }, [currentTheme]);

    const switchTheme = (themeName) => {
        if (themes[themeName]) {
            setCurrentTheme(themeName);
        }
    };

    const value = {
        currentTheme,
        themes,
        switchTheme,
        theme: themes[currentTheme]
    };

    return (
        <ThemeContext.Provider value={value}>
            {children}
        </ThemeContext.Provider>
    );
}