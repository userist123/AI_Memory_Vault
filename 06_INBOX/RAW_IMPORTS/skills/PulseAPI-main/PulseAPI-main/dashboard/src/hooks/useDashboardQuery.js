import { useQuery } from '@tanstack/react-query';
import { analyticsApi } from '../api/api';
import { QUERY_KEYS, REFETCH_INTERVAL } from '../constants';

/**
 * Fetches dashboard data for a given time range.
 * @param {Object} params - { startTime?: string (ISO), endTime?: string (ISO) }
 * @param {Object} options - Extra react-query options
 */
export function useDashboardQuery(params = {}, options = {}) {
    return useQuery({
        // Include params in the query key so a time-range change auto-refetches
        queryKey: [...QUERY_KEYS.DASHBOARD, params],
        queryFn: () => analyticsApi.getDashboard(params),
        refetchInterval: REFETCH_INTERVAL,
        placeholderData: (previousData) => previousData,
        ...options,
    });
}
