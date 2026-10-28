FROM python:3.12-slim AS runtime
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app/services/research-worker
COPY services/research-worker/requirements.txt ./requirements.txt
RUN pip install --no-cache-dir --upgrade pip && pip install --no-cache-dir -r requirements.txt
COPY services/research-worker/api ./api
COPY services/research-worker/agent ./agent
COPY packages/contracts/research /app/packages/contracts/research
RUN useradd --create-home --uid 10001 appuser && mkdir -p ./agent/output && chown -R appuser:appuser /app
USER appuser
EXPOSE 8000
CMD ["uvicorn", "api.main:app", "--host", "0.0.0.0", "--port", "8000"]
