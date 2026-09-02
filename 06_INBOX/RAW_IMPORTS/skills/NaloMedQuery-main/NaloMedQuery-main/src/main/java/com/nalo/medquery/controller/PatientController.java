package com.nalo.medquery.controller;

import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.web.PageableDefault;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.util.UriComponentsBuilder;

import com.nalo.medquery.domain.entity.PatientEntity;
import com.nalo.medquery.domain.model.patient.DataListAllPatients;
import com.nalo.medquery.domain.model.patient.DetailedPatientDataDTO;
import com.nalo.medquery.domain.model.patient.PatientRegisterData;
import com.nalo.medquery.domain.model.patient.PatientUpdatableData;
import com.nalo.medquery.domain.repository.IPatientRepository;

import jakarta.validation.Valid;

import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.PathVariable;

import io.swagger.v3.oas.annotations.security.SecurityRequirement;

@RestController
@RequestMapping("/patients")
@SecurityRequirement(name = "bearer-key")
public class PatientController {

    @Autowired
    private IPatientRepository repository;

    @PostMapping
    @Transactional
    public ResponseEntity registerPatient(@RequestBody @Valid PatientRegisterData entity, UriComponentsBuilder uriBuilder) {
        var patient = new PatientEntity(entity);
        repository.save(patient);

        var uri = uriBuilder
                    .path("/patients/{id}")
                    .buildAndExpand(patient.getId())
                    .toUri();

        return ResponseEntity
                .created(uri)
                .body(new DetailedPatientDataDTO(patient));
    }

    @GetMapping
    public ResponseEntity<Page<DataListAllPatients>> listAllPatients(@PageableDefault(size = 10, sort = {"nome"}) Pageable page){
        return ResponseEntity.ok(repository.findAllByAtivoTrue(page).map(DataListAllPatients::new));
    }

    @GetMapping("/{id}")
    public ResponseEntity listOnePatient(@PathVariable Long id){
        var patient = repository.getReferenceById(id);
        return ResponseEntity.ok(new DetailedPatientDataDTO(patient));
    }
    
    @PutMapping
    public ResponseEntity updatePatientInfo(@RequestBody @Valid PatientUpdatableData data) {
        var patientEntity = repository.getReferenceById(data.id());
        patientEntity.updatePatientInformation(data);
        return ResponseEntity.ok(new DetailedPatientDataDTO(patientEntity));
    }

    @DeleteMapping("/{id}")
    @Transactional
    public ResponseEntity inactivatePatient(@PathVariable Long id){
        var patientEntity = repository.getReferenceById(id);
        patientEntity.setInactive();
        return ResponseEntity.noContent().build();
    }

    @DeleteMapping("/delete/{id}")
    @Transactional
    public ResponseEntity deletePatientPermanently(@PathVariable Long id){
        repository.deleteById(id);
        return ResponseEntity.noContent().build();
    }
}
