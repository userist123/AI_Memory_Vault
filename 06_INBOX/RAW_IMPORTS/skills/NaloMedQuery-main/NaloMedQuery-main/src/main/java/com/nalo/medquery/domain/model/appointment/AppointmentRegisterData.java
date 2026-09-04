package com.nalo.medquery.domain.model.appointment;

import java.time.LocalDateTime;

import jakarta.validation.constraints.Future;
import jakarta.validation.constraints.NotNull;

import com.nalo.medquery.domain.model.doctor.EnumDoctorSpecialty;

public record AppointmentRegisterData(
    Long doctorId,
       
    @NotNull    
    Long patientId,
    @NotNull 
    @Future   
    LocalDateTime dia,
    EnumDoctorSpecialty especialidade
) {}