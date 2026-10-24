export default function robots(){ return { rules: [{ userAgent: '*', allow: '/', disallow: ['/go/', '/api/', '/p/'] }] }; }
