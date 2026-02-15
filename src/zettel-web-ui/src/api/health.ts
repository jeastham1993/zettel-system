import { get } from './client'
import type { HealthReport } from './types'

export function getHealth(): Promise<HealthReport> {
  return get<HealthReport>('/health')
}
