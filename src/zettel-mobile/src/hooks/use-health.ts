import { useQuery } from '@tanstack/react-query'
import { getHealth } from '../api/health'
import { queryKeys } from './query-keys'

export function useHealth(refetchInterval = 30_000) {
  return useQuery({
    queryKey: queryKeys.health.all,
    queryFn: getHealth,
    refetchInterval,
    refetchIntervalInBackground: false,
  })
}
