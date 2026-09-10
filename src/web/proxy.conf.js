const target = process.env.API_HTTPS || process.env.API_HTTP;

if (!target) {
  throw new Error(
    'API endpoint is not configured. Run the app through Aspire.'
  );
}

module.exports = {
  '/api': {
    target,
    secure: false,
    changeOrigin: true,
  },
};
