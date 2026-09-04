package com.nalo.medquery.controller;

import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.web.util.UriComponentsBuilder;
import org.springframework.data.domain.Page;
import org.springframework.data.domain.Pageable;
import org.springframework.data.web.PageableDefault;
import org.springframework.http.ResponseEntity;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.PathVariable;

import com.nalo.medquery.domain.entity.DoctorEntity;
import com.nalo.medquery.domain.model.doctor.DataListAllDoctors;
import com.nalo.medquery.domain.model.doctor.DetailedDoctorDataDTO;
import com.nalo.medquery.domain.model.doctor.DoctorRegisterData;
import com.nalo.medquery.domain.model.doctor.DoctorUpdatableData;
import com.nalo.medquery.domain.repository.IDoctorRepository;

import io.swagger.v3.oas.annotations.security.SecurityRequirement;

import jakarta.validation.Valid;

@RestController
@RequestMapping("/doctors")
@SecurityRequirement(name = "bearer-key")
public class DoctorController {

    @Autowired
    private IDoctorRepository repository;
    
    @PostMapping 
    @Transactional
    public ResponseEntity registerDoctors(@RequestBody @Valid DoctorRegisterData data, UriComponentsBuilder uriBuilder) {                
        var doctor = new DoctorEntity(data);
        repository.save(doctor);
        
        var uri = uriBuilder
                    .path("/doctors/{id}")
                    .buildAndExpand(doctor.getId())
                    .toUri();

        return ResponseEntity
                .created(uri)
                .body(new DetailedDoctorDataDTO(doctor));
    }

    @GetMapping
    public ResponseEntity<Page<DataListAllDoctors>> listAllDoctors(@PageableDefault(size = 10, sort = {"nome"}) Pageable page){
        return ResponseEntity.ok(repository.findAllByActiveTrue(page).map(DataListAllDoctors::new));
    }

    @GetMapping("/{id}")
    public ResponseEntity listOneDoctor(@PathVariable Long id){
        var doctor = repository.getReferenceById(id);        
        return ResponseEntity.ok(new DetailedDoctorDataDTO(doctor));
    }

    @PutMapping
    @Transactional
    public ResponseEntity updateDoctorsInfo(@RequestBody @Valid DoctorUpdatableData data) {
        var doctorEntity = repository.getReferenceById(data.id());
        doctorEntity.updateDoctorInformation(data);
        // I do this cause it is not good to use a entity directly in the controller response
        return ResponseEntity.ok(new DetailedDoctorDataDTO(doctorEntity)); 
    }
 
    //logical delete
    @DeleteMapping("/{id}")
    @Transactional
    public ResponseEntity inactivateDoctor(@PathVariable Long id){
        var doctorEntity = repository.getReferenceById(id);
        doctorEntity.setInactive();

        return ResponseEntity.noContent().build();
    }

    @DeleteMapping("/delete/{id}")
    @Transactional
    public ResponseEntity deleteDoctorPermanently(@PathVariable Long id){
        repository.deleteById(id);

        return ResponseEntity.noContent().build();
    }

}
