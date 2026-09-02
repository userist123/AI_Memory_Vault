package com.nalo.medquery.controller;

import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.http.ResponseEntity;

import jakarta.validation.Valid;

import com.nalo.medquery.domain.model.appointment.AppointmentRegisterData;
import com.nalo.medquery.domain.model.appointment.DetailedAppointmentDataDTO;
import com.nalo.medquery.domain.repository.IAppointmentRepository;
import com.nalo.medquery.domain.service.AppointmentService;

import io.swagger.v3.oas.annotations.security.SecurityRequirement;

@RestController
@RequestMapping("/appointments")
@SecurityRequirement(name = "bearer-key")
public class AppointmentController {

    @Autowired
    private AppointmentService appointmentService;

    @Autowired
    private IAppointmentRepository repository;

    @PostMapping 
    @Transactional
    public ResponseEntity<DetailedAppointmentDataDTO> registerAppointment(@RequestBody @Valid AppointmentRegisterData data){
        var dto = appointmentService.schedule(data);
        return ResponseEntity.ok(dto);
    }

    @DeleteMapping("/{id}")
    @Transactional
    public ResponseEntity<Void> cancelAppointment(@PathVariable Long id){
        repository.deleteById(id);
        return ResponseEntity.noContent().build();
    }

}
