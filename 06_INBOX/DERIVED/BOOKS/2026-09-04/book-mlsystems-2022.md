# Designing Machine Learning Systems
**Author:** Chip Huyen
**Source:** `06_INBOX/RAW_IMPORTS/BOOKS/_OceanofPDF.com_Designing_Machine_Learning_Systems_An_Iterative_Process_for_Production-Ready_Applications_-_Chip_Huyen.pdf`
**SHA-256:** `e3b542b0350a4c24f155fee8eb09df08e742a7fe7508873af48da11b3424fc70`
**Pages:** 461

## Processing status
`PROCESSED_TO_PROVISIONAL_CANDIDATES`

## Chapter map
- Chapter 1: Overview of Machine Learning Systems
- Chapter 2: Introduction to Machine Learning Systems Design
- Chapter 3: Data Engineering
- Chapter 4: Training Data
- Chapter 5: Feature Engineering
- Chapter 6: Model Development and Offline Evaluation
- Chapter 7: Model Deployment
- Chapter 8: Data Distribution Shifts and Monitoring
- Chapter 9: Continual Learning
- Chapter 10: Infrastructure and Tooling
- Chapter 11: The Human Side of Machine Learning

## Candidate knowledge seeds
### book-mlsystems-2022-c001 - PRINCIPLE
A machine learning system is appropriate only when the problem has learnable patterns, available/collectable data, predictive structure, and enough repetition or scale to justify learning infrastructure.
- Source locator: `CHAPTER 1`
- Source SHA-256: `e3b542b0350a4c24f155fee8eb09df08e742a7fe7508873af48da11b3424fc70`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-mlsystems-2022-c002 - PRINCIPLE
ML development is iterative: data, labels, features, models, evaluation, deployment, and monitoring feed back into repeated cycles rather than a one-shot pipeline.
- Source locator: `CHAPTER 1`
- Source SHA-256: `e3b542b0350a4c24f155fee8eb09df08e742a7fe7508873af48da11b3424fc70`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-mlsystems-2022-c003 - PRINCIPLE
Training data quality and label quality can dominate model behavior; data engineering is therefore part of model quality, not merely preprocessing.
- Source locator: `CHAPTERS 3-4`
- Source SHA-256: `e3b542b0350a4c24f155fee8eb09df08e742a7fe7508873af48da11b3424fc70`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-mlsystems-2022-c004 - PRINCIPLE
Feature engineering and model development should be aligned with the real distribution and constraints of the deployment environment.
- Source locator: `CHAPTERS 5-6`
- Source SHA-256: `e3b542b0350a4c24f155fee8eb09df08e742a7fe7508873af48da11b3424fc70`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-mlsystems-2022-c005 - PRINCIPLE
Offline evaluation can be misleading when the test set is not representative of the deployed traffic or when leakage and selection effects distort the measurement.
- Source locator: `CHAPTER 6`
- Source SHA-256: `e3b542b0350a4c24f155fee8eb09df08e742a7fe7508873af48da11b3424fc70`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-mlsystems-2022-c006 - PRINCIPLE
Deployment turns a trained model into a production system with latency, reliability, resource, and integration constraints.
- Source locator: `CHAPTER 7`
- Source SHA-256: `e3b542b0350a4c24f155fee8eb09df08e742a7fe7508873af48da11b3424fc70`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-mlsystems-2022-c007 - PRINCIPLE
Distribution shifts can degrade a model after deployment; monitoring needs to detect changes in inputs, outputs, labels, and system behavior.
- Source locator: `CHAPTER 8`
- Source SHA-256: `e3b542b0350a4c24f155fee8eb09df08e742a7fe7508873af48da11b3424fc70`
- Verification: `CANDIDATE`; human-gated promotion required.

### book-mlsystems-2022-c008 - PRINCIPLE
Continual learning is a design problem involving when to retrain, what data to retain, and how to avoid degrading previously learned behavior.
- Source locator: `CHAPTER 9`
- Source SHA-256: `e3b542b0350a4c24f155fee8eb09df08e742a7fe7508873af48da11b3424fc70`
- Verification: `CANDIDATE`; human-gated promotion required.
