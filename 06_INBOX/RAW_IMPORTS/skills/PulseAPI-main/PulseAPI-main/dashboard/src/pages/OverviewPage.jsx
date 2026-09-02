import { useState, useMemo, useCallback } from 'react';
import { useDashboardQuery } from '../hooks/useDashboardQuery';
import { useAuth } from '../contexts/AuthContext';
import StatsGrid from '../components/StatsGrid';
import TopEndpoints from '../components/TopEndpoints';
import TimeRangeSelector from '../components/TimeRangeSelector';
import { ApiHitsChart, StatusDistributionChart } from '../components/charts';
import { PageStatus } from '../components/ui';
import styles from '../styles/modules/pages/PageComponents.module.scss';

// Default: last 24 hours
function getDefaultRange() {
    const endTime   = new Date();
    const startTime = new Date(endTime.getTime() - 24 * 60 * 60 * 1000);
    return { startTime: startTime.toISOString(), endTime: endTime.toISOString() };
}

export function OverviewPage() {
    const { user } = useAuth();

    // Active preset tracking ('1h', '4h', '24h', '7d', '30d', 'custom')
    const [selectedPreset, setSelectedPreset] = useState('24h');
    const [timeRange, setTimeRange]           = useState(getDefaultRange);

    const handleRangeChange = useCallback(({ startTime, endTime, preset }) => {
        setTimeRange({ startTime, endTime });
        if (preset) setSelectedPreset(preset);
    }, []);

    const { data, isPending, isFetching, error, refetch } = useDashboardQuery(timeRange);

    const stats        = data?.data?.stats        ?? null;
    const topEndpoints = data?.data?.topEndpoints ?? [];

    const statusData = useMemo(() => {
        if (!stats) return null;
        return {
            labels: ['Success (2xx)', 'Errors (4xx/5xx)'],
            values: [stats.successHits, stats.errorHits],
        };
    }, [stats]);

    if ((isPending && !data) || error) {
        return (
            <PageStatus
                isLoading={isPending && !data}
                error={error}
                onRetry={refetch}
                loadingText="Loading dashboard..."
                errorText="Failed to load dashboard data"
            />
        );
    }

    return (
        <div className={styles.pageContainer}>
            {/* Header row with title + time range selector */}
            <div className={styles.headerWithActions}>
                <div className={styles.pageHeader}>
                    <div className="flex items-center gap-3">
                        <h2>Overview</h2>
                        {isFetching && (
                            <span className="text-xs font-mono text-primary animate-pulse bg-primary/10 px-2 py-0.5 rounded-full border border-primary/20">
                                Refreshing...
                            </span>
                        )}
                    </div>
                    <p>API monitoring dashboard</p>
                </div>

                <TimeRangeSelector
                    value={selectedPreset}
                    onChange={handleRangeChange}
                />
            </div>

            <StatsGrid stats={stats} />

            <div className={styles.gridTwoCols}>
                <ApiHitsChart stats={stats} />
                <StatusDistributionChart data={statusData} />
            </div>

            <TopEndpoints endpoints={topEndpoints} />
        </div>
    );
}
