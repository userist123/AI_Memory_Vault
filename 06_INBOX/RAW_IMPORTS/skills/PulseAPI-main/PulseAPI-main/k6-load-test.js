import http from 'k6/http';
import { check, sleep } from 'k6';

// Configuration for the load test
export const options = {
  stages: [
    { duration: '5s', target: 50 },  // Ramp up to 50 concurrent users over 5 seconds
    { duration: '15s', target: 50 }, // Keep 50 concurrent users for 15 seconds
    { duration: '5s', target: 0 },   // Ramp down to 0 users over 5 seconds
  ],
  thresholds: {
    // 95% of requests must complete below 500ms
    http_req_duration: ['p(95)<500'],
    // Error rate should be less than 1%
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const url = 'http://localhost:5000/api/client/auth/login';

  const payload = JSON.stringify({
    email: 'google@gmail.com', // Replace with a valid client email
    password: 'Google@123',     // Replace with a valid password
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
  };

  // Perform the POST request
  const res = http.post(url, payload, params);

  // Validate the response
  check(res, {
    'status is 200': (r) => r.status === 200,
    'login successful': (r) => r.body.includes('success":true'),
  });

  // Think time: Wait a short amount of time between requests to simulate real users
  sleep(1);
}
