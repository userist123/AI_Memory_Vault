import { useState } from 'react';
import { Calendar, Clock, ChevronDown, X } from 'lucide-react';
import styles from '../styles/modules/ui/TimeRangeSelector.module.scss';

const PRESETS = [
    { label: '1H', value: '1h', hours: 1 },
    { label: '4H', value: '4h', hours: 4 },
    { label: '24H', value: '24h', hours: 24 },
    { label: '7D', value: '7d', hours: 168 },
    { label: '30D', value: '30d', hours: 720 },
];

/** 
 * TimeRangeSelector
 * Props:
 *   onChange({ startTime, endTime }) — called whenever the user picks a range.
 *   defaultPreset — one of the preset values above (default '24h').
 */
export default function TimeRangeSelector({ onChange, defaultPreset = '24h' }) {
    const [selected, setSelected] = useState(defaultPreset);
    const [showCustom, setShowCustom] = useState(false);
    const [customStart, setCustomStart] = useState('');
    const [customEnd, setCustomEnd] = useState('');
    const [customError, setCustomError] = useState('');

    const applyPreset = (preset) => {
        setSelected(preset.value);
        setShowCustom(false);
        setCustomError('');

        const endTime = new Date();
        const startTime = new Date(endTime.getTime() - preset.hours * 60 * 60 * 1000);
        onChange({ startTime: startTime.toISOString(), endTime: endTime.toISOString() });
    };

    const applyCustom = () => {
        if (!customStart || !customEnd) {
            setCustomError('Please select both start and end date/time.');
            return;
        }
        const start = new Date(customStart);
        const end = new Date(customEnd);
        if (start >= end) {
            setCustomError('Start must be before end.');
            return;
        }
        setCustomError('');
        setSelected('custom');
        setShowCustom(false);
        onChange({ startTime: start.toISOString(), endTime: end.toISOString() });
    };

    const clearCustom = () => {
        setSelected('24h');
        setCustomStart('');
        setCustomEnd('');
        setShowCustom(false);
        setCustomError('');
        // Re-apply 24h default
        const endTime = new Date();
        const startTime = new Date(endTime.getTime() - 24 * 60 * 60 * 1000);
        onChange({ startTime: startTime.toISOString(), endTime: endTime.toISOString() });
    };

    // Format a datetime-local string to a readable label
    const formatCustomLabel = () => {
        if (!customStart || !customEnd) return 'Custom';
        const fmt = (d) => new Date(d).toLocaleString('en-IN', {
            day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', hour12: true
        });
        return `${fmt(customStart)} → ${fmt(customEnd)}`;
    };

    // Max value for date inputs = now (can't pick future)
    const nowLocal = new Date(Date.now() - new Date().getTimezoneOffset() * 60000)
        .toISOString()
        .slice(0, 16);

    return (
        <div className={styles.wrapper}>
            {/* Preset pill buttons */}
            <div className={styles.presets}>
                <Clock className={styles.icon} />
                {PRESETS.map((p) => (
                    <button
                        key={p.value}
                        className={`${styles.presetBtn} ${selected === p.value ? styles.active : ''}`}
                        onClick={() => applyPreset(p)}
                        title={`Last ${p.label}`}
                    >
                        {p.label}
                    </button>
                ))}

                {/* Custom range toggle */}
                <button
                    className={`${styles.presetBtn} ${styles.customBtn} ${selected === 'custom' ? styles.active : ''}`}
                    onClick={() => setShowCustom((v) => !v)}
                    title="Custom time range"
                >
                    <Calendar className={styles.btnIcon} />
                    {selected === 'custom' ? formatCustomLabel() : 'Custom'}
                    <ChevronDown className={`${styles.chevron} ${showCustom ? styles.open : ''}`} />
                </button>

                {/* Clear custom */}
                {selected === 'custom' && (
                    <button className={styles.clearBtn} onClick={clearCustom} title="Clear custom range">
                        <X className={styles.btnIcon} />
                    </button>
                )}
            </div>

            {/* Custom date-time picker dropdown */}
            {showCustom && (
                <div className={styles.customPanel}>
                    <p className={styles.panelTitle}>Select Custom Time Range</p>

                    <div className={styles.inputRow}>
                        <div className={styles.inputGroup}>
                            <label className={styles.label}>Start</label>
                            <input
                                type="datetime-local"
                                className={styles.dateInput}
                                value={customStart}
                                max={customEnd || nowLocal}
                                onChange={(e) => setCustomStart(e.target.value)}
                            />
                        </div>
                        <div className={styles.inputGroup}>
                            <label className={styles.label}>End</label>
                            <input
                                type="datetime-local"
                                className={styles.dateInput}
                                value={customEnd}
                                min={customStart}
                                max={nowLocal}
                                onChange={(e) => setCustomEnd(e.target.value)}
                            />
                        </div>
                    </div>

                    {customError && (
                        <p className={styles.error}>{customError}</p>
                    )}

                    <div className={styles.panelActions}>
                        <button className={styles.cancelBtn} onClick={() => setShowCustom(false)}>
                            Cancel
                        </button>
                        <button className={styles.applyBtn} onClick={applyCustom}>
                            Apply Range
                        </button>
                    </div>
                </div>
            )}
        </div>
    );
}
