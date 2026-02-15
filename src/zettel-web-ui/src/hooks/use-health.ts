import { useQuery } from '@tanstack/react-query'
import { getHealth } from '@/api/health'

export function useHealth(refetchInterval = 30_000) {
  return useQuery({
    queryKey: ['health'],
    queryFn: getHealth,
    refetchInterval,
  })
}
