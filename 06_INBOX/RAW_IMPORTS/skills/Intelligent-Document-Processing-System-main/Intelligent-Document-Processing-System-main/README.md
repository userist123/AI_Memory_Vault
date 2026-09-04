# Intelligent Document Parser

An end-to-end backend system for extracting structured information from PDFs and images using OCR and transformer-based NLP models.

The project focuses on **production-style system design** rather than model experimentation. It demonstrates how machine learning components can be integrated into a scalable backend pipeline for document processing.

The system processes uploaded documents asynchronously, extracts raw text using OCR, identifies entities using a pretrained NER model, and returns structured data via a REST API.



## Key Features

- Document ingestion via REST API
- PDF and image processing
- OCR-based text extraction
- Transformer-based Named Entity Recognition (NER)
- Asynchronous job processing
- Status tracking for processing jobs
- Structured JSON output
- Production-style backend architecture



## System Architecture

The project is designed using a layered architecture commonly used in production ML systems.

```
Client
  |
  v
FastAPI API Layer
  |
  v
Service Layer
  |
  v
Async Job Queue
  |
  v
Document Processing Pipeline
    ├── Text Extraction (OCR)
    ├── NLP Processing (NER)
    └── Entity Post-processing
  |
  v
Persistence Layer
```

### Processing Flow

1. Client uploads a document (PDF or image).
2. API stores the document and creates a processing job.
3. The job is queued for asynchronous processing.
4. Worker retrieves the job and executes the pipeline:
   - Extract text using OCR
   - Run transformer-based NER to identify entities
   - Post-process entities
5. Extracted data is stored and returned as structured JSON.



## Project Structure

```
app/
 ├── api/
 │   └── endpoints
 ├── core/
 │   ├── config
 │   ├── logging
 │   └── security
 ├── pipeline/
 │   ├── text_extractor
 │   ├── ocr_processor
 │   ├── ner_processor
 │   └── pipeline.py
 ├── services/
 │   ├── document_service
 │   └── job_service
 ├── workers/
 │   ├── worker
 │   └── tasks
 ├── persistence/
 │   ├── database
 │   ├── models
 │   └── repositories
 ├── messaging/
 │   └── queue
 └── schemas
```

The architecture separates concerns between:

- API handling
- business logic
- ML pipeline
- async workers
- persistence



## Technologies Used

### Backend

- FastAPI
- Python
- Async workers
- REST APIs

### Machine Learning

- Transformer-based NER
- OCR for document text extraction

### Infrastructure

- Docker
- Redis (message broker)
- PostgreSQL (persistence)



## Example Output

```json
{
  "document_id": "123",
  "status": "COMPLETED",
  "entities": {
    "invoice_number": "INV-1024",
    "vendor": "ABC Pvt Ltd",
    "total_amount": "₹12,450",
    "date": "2024-01-10"
  }
}
```



## Running the Project

### Clone the repository

```bash
git clone https://github.com/yourusername/intelligent-document-parser.git
cd intelligent-document-parser
```

### Install dependencies

```bash
pip install -r requirements
```

### Run the API

```bash
uvicorn app.main:app --reload
```



## Design Goals

This project was built to demonstrate:

- Integration of ML models into backend systems
- Asynchronous document processing pipelines
- Clean separation between API, services, and ML components
- Scalable architecture for real-world document processing workflows



## Limitations

Invoice and receipt formats vary widely across organizations.  
The current system focuses on demonstrating **pipeline architecture and ML integration**, rather than solving layout variability across all document types.

Improving extraction accuracy would require:

- layout-aware models
- specialized invoice datasets
- domain-specific model fine-tuning



## Future Improvements

- Layout-aware document understanding models
- Table extraction
- Improved entity post-processing
- Multi-document type classification
- Production deployment on cloud infrastructure
