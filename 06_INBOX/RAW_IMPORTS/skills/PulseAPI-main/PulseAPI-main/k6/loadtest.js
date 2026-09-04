import http from 'k6/http';
import { check } from 'k6';
import { Trend, Counter } from 'k6/metrics';

// ============================================================================
// CONFIGURATION
// ============================================================================
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const API_KEY = __ENV.API_KEY || 'apim_6ec82059bbfc91dd177ec3a541178c85af47bae8';
const INGEST_ENDPOINT = `${BASE_URL}/api/hit`;

// ============================================================================
// CUSTOM METRICS
// ============================================================================
const ingestionLatency = new Trend('ingestion_latency');

const status202Count = new Counter('status_202');
const status400Count = new Counter('status_400');
const status401Count = new Counter('status_401');
const status429Count = new Counter('status_429');
const status500Count = new Counter('status_500');
const statusOtherCount = new Counter('status_other');

// ============================================================================
// TEST CONFIGURATION
// ============================================================================
export const options = {
  vus: 300,
  duration: '5m',
  thresholds: {
    // 95% of requests should complete under 1 sec
    http_req_duration: ['p(95)<1000'],
    // Error rate less than 1%
    http_req_failed: ['rate<0.01'],
  },
};

// ============================================================================
// MAIN WORKLOAD FUNCTION
// ============================================================================
export default function () {
  const payload = JSON.stringify({
    serviceName: 'user-service',
    endpoint: '/api/users/fiftyforten',
    method: 'GET',
    statusCode: 200,
    latencyMs: 50,
    timestamp: new Date().toISOString(),
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'x-api-key': API_KEY,
    },
  };

  const res = http.post(INGEST_ENDPOINT, payload, params);

  // Track custom latency
  ingestionLatency.add(res.timings.duration);

  // Track HTTP status distribution safely with breaks
  switch (res.status) {
    case 202:
    case 200:
      status202Count.add(1);
      break;
    case 400:
      status400Count.add(1);
      break;
    case 401:
      status401Count.add(1);
      break;
    case 429:
      status429Count.add(1);
      break;
    case 500:
      status500Count.add(1);
      break;
    default:
      statusOtherCount.add(1);
      break;
  }

  check(res, {
    'status is successful': (r) => r.status === 202 || r.status === 200,
  });
}

// ============================================================================
// SUMMARY REPORT
// ============================================================================
export function handleSummary(data) {
  const getMetricCount = (name) =>
    data.metrics && data.metrics[name] && data.metrics[name].values
      ? data.metrics[name].values.count || 0
      : 0;

  const getMetricVal = (metricObj, key, defaultValue = '0.00') =>
    metricObj && metricObj[key] !== undefined ? metricObj[key].toFixed(2) : defaultValue;

  const latencyValues = data.metrics.ingestion_latency ? data.metrics.ingestion_latency.values : {};
  const httpDurationValues = data.metrics.http_req_duration ? data.metrics.http_req_duration.values : {};

  const totalRequests = data.metrics.http_reqs ? data.metrics.http_reqs.values.count : 0;
  const failedRate = data.metrics.http_req_failed ? data.metrics.http_req_failed.values.rate : 0;
  const testDurationSec = data.state && data.state.testRunDurationMs ? data.state.testRunDurationMs / 1000 : 1;
  const rps = totalRequests / testDurationSec;

  const summary = `
=================================================
LOAD TEST PERFORMANCE SUMMARY
=================================================

TOTAL REQUESTS
--------------
${totalRequests}

THROUGHPUT
-----------
Requests/sec : ${rps.toFixed(2)} RPS

LATENCY (Custom Ingestion)
--------------------------
Average : ${getMetricVal(latencyValues, 'avg')} ms
p95     : ${getMetricVal(latencyValues, 'p(95)')} ms
p99     : ${getMetricVal(latencyValues, 'p(99)')} ms
Max     : ${getMetricVal(latencyValues, 'max')} ms
Min     : ${getMetricVal(latencyValues, 'min')} ms

HTTP REQ DURATION
-----------------
Average : ${getMetricVal(httpDurationValues, 'avg')} ms
p95     : ${getMetricVal(httpDurationValues, 'p(95)')} ms
p99     : ${getMetricVal(httpDurationValues, 'p(99)')} ms

ERROR RATE
----------
${(failedRate * 100).toFixed(2)}%

STATUS CODE BREAKDOWN
---------------------
202 Accepted     : ${getMetricCount('status_202')}
400 Bad Request  : ${getMetricCount('status_400')}
401 Unauthorized : ${getMetricCount('status_401')}
429 Rate Limited : ${getMetricCount('status_429')}
500 Internal Err : ${getMetricCount('status_500')}
Other            : ${getMetricCount('status_other')}

=================================================
`;

  return {
    stdout: summary,
  };
}